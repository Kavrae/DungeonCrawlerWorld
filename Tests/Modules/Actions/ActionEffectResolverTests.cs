using Game.Blueprints;
using Engine.ECS.Entities;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActionEffectResolverTests
{
    private static readonly EntityKeys Keys = new();

    private const int SourceEntityId = 1;
    private const int BlockingTargetEntityId = 2;
    private const int NonBlockingTargetEntityId = 3;
    private const int SecondNonBlockingTargetEntityId = 4;
    private static readonly Vector3Int TargetTile = new(5, 5, 0);
    private static readonly ActionDefinition Action = new(
        Guid.NewGuid(), "Test Attack", null, "#", default, [],
        Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 15, MaxFlatDamage: 15)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));

    /// <summary>Records every ApplyStack call it receives instead of touching any real component pool -- keeps these tests independent of any concrete effect (Burning/Poison/Paralysis).</summary>
    private sealed class FakeStatusEffectAuraApplier(StatusEffectType effectType) : IStatusEffectAuraApplier
    {
        public StatusEffectType EffectType { get; } = effectType;
        public List<(int EntityId, ActionSource Source)> AppliedCalls { get; } = [];

        public int GetCurrentStackCount(int entityId) => AppliedCalls.Count(call => call.EntityId == entityId);

        public void ApplyStack(int entityId, ActionSource source, long now) => AppliedCalls.Add((entityId, source));
    }

    /// <summary>Never rolls a crit -- NextDouble always returns 1.0, comfortably above any crit chance -- so damage-amount assertions in these orchestration tests stay deterministic.</summary>
    /// <summary>Minimal IMapQuery test double supporting both the Blocking slot and the general occupant index -- everything else is unused by ActionEffectResolver.Apply.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blockingByPosition = [];
        private readonly Dictionary<Vector3Int, List<int>> _occupantsByPosition = [];

        public MapBounds Bounds { get; } = new(0, 0, 100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }

        public void SetBlockingOccupant(Vector3Int position, int entityId)
        {
            _blockingByPosition[position] = entityId;
            AddOccupant(position, entityId);
        }

        public void AddNonBlockingOccupant(Vector3Int position, int entityId) => AddOccupant(position, entityId);

        private void AddOccupant(Vector3Int position, int entityId)
        {
            if (!_occupantsByPosition.TryGetValue(position, out var entityIds))
            {
                entityIds = [];
                _occupantsByPosition[position] = entityIds;
            }

            entityIds.Add(entityId);
        }

        public int GetEntityIdAt(Vector3Int position) => _blockingByPosition.TryGetValue(position, out var id) ? id : -1;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            _occupantsByPosition.TryGetValue(position, out var entityIds) ? entityIds : [];
    }

    private static (FakeMapQuery MapQuery, PackedComponentPool<SimpleHealthComponent> Health, EventBus EventBus, MathUtility MathUtility, StatusEffectAuraApplierRegistry StatusEffectAppliers, ComponentManager ComponentManager) Build()
    {
        var mapQuery = new FakeMapQuery();
        var health = new PackedComponentPool<SimpleHealthComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        var eventBus = new EventBus();
        var mathUtility = new MathUtility();
        var statusEffectAppliers = new StatusEffectAuraApplierRegistry();
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));

        return (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager);
    }

    [TestMethod]
    public void Apply_BlockingOccupantAtTargetTile_DamagesIt()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>The requirement this test guards: a tile-targeted action must hit Tiny/Phasing entities too, not just the single Blocking occupant Map's own array can answer for.</summary>
    [TestMethod]
    public void Apply_NonBlockingEntityAtTargetTile_DamagesItEvenWithNoBlockingOccupant()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new SimpleHealthComponent(100, 100));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>Stacked non-Blocking entities (e.g. several Tiny goblins sharing a cell) must all be hit by the same activation, not just the first.</summary>
    [TestMethod]
    public void Apply_MultipleNonBlockingEntitiesStackedAtTargetTile_DamagesAllOfThem()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, SecondNonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        health.Add(SecondNonBlockingTargetEntityId, new SimpleHealthComponent(100, 100));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(SecondNonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>A Blocking occupant and a Phasing entity can legitimately overlap the same tile -- both must be damaged by one activation, not just one or the other.</summary>
    [TestMethod]
    public void Apply_BlockingOccupantOverlappingNonBlockingEntity_DamagesBoth()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        health.Add(NonBlockingTargetEntityId, new SimpleHealthComponent(100, 100));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoOccupantsAtTargetTile_DoesNothing()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
    }

    private static readonly ActionDefinition StrengthTaggedAction = new(
        Guid.NewGuid(), "Test Strength Attack", null, "#", default, [Tag.Strength],
        Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 15, MaxFlatDamage: 15)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));

    [TestMethod]
    public void Apply_ActionTaggedWithMatchingAbilityScore_AddsScoreTotalToBaseDamage()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        abilityScores.Add(SourceEntityId, AbilityScoreTestPools.Score(AbilityScoreType.Strength, baseValue: 8, total: 8));

        TestActionEffects.Apply(StrengthTaggedAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, statModifiers: null, deadEntities: null, abilityScores: abilityScores);

        // 15 base damage + 8 Strength Total = 23.
        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 23, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_AbilityScoresPoolPresentButSourceHasNoMatchingScore_NoBonus()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        // SourceEntityId has no ability scores at all.

        TestActionEffects.Apply(StrengthTaggedAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, statModifiers: null, deadEntities: null, abilityScores: abilityScores);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    private static readonly ActionDefinition ActionWithStatusEffect = new(
        Guid.NewGuid(), "Test Status Effect Attack", null, "#", default, [],
        Effects: [new ActionEffect([new StatusEffectGrant(StatusEffectType.Paralysis)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));

    [TestMethod]
    public void Apply_BlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.HasCount(1, applier.AppliedCalls);
        Assert.AreEqual(BlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
        Assert.AreEqual(ActionSource.FromEntity(componentManager, Keys, SourceEntityId, creatures: new BlueprintRegistry()), applier.AppliedCalls[0].Source);
    }

    [TestMethod]
    public void Apply_NonBlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.HasCount(1, applier.AppliedCalls);
        Assert.AreEqual(NonBlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
    }

    /// <summary>The concrete "immortal but affectable" regression: the target never gets a SimpleHealthComponent at all, and the status effect still grants.</summary>
    [TestMethod]
    public void Apply_TargetWithNoHealthComponentAtAll_StillGrantsStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
        Assert.HasCount(1, applier.AppliedCalls);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotThrow()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);
    }

    [TestMethod]
    public void Apply_StatusEffectGranted_PublishesStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        statusEffectAppliers.Register(new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis));
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        StatusEffectAppliedEvent? published = null;
        eventBus.Subscribe<StatusEffectAppliedEvent>(e => published = e);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(BlockingTargetEntityId, published!.Value.EntityId);
        Assert.AreEqual(StatusEffectType.Paralysis, published.Value.EffectType);
        Assert.AreEqual(ActionSource.FromEntity(componentManager, Keys, SourceEntityId, creatures: new BlueprintRegistry()), published.Value.Source);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotPublishStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        var published = false;
        eventBus.Subscribe<StatusEffectAppliedEvent>(_ => published = true);

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.IsFalse(published);
    }

    /// <summary>A corpse doesn't receive newly-granted status effects -- see DeathSystem/DeadComponent.</summary>
    [TestMethod]
    public void Apply_TargetIsDead_DoesNotGrantStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        componentManager.GetPackedPool<DeadComponent>().Add(BlockingTargetEntityId, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        TestActionEffects.Apply(ActionWithStatusEffect, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, statModifiers: null, componentManager.GetPackedPool<DeadComponent>());

        Assert.IsEmpty(applier.AppliedCalls);
    }

    private static readonly ActionDefinition DodgeableAction = new(
        Guid.NewGuid(), "Test Dodgeable Attack", null, "#", default, [Tag.Dodgeable],
        Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 15, MaxFlatDamage: 15)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));

    [TestMethod]
    public void Apply_DodgeableAction_SkipsTargetCurrentlyDodging()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var dodgingEntities = new PackedComponentPool<DodgingComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        dodgingEntities.Add(BlockingTargetEntityId, new DodgingComponent(expiresAtFrame: 30));

        TestActionEffects.Apply(DodgeableAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, dodgingEntities: dodgingEntities);

        Assert.AreEqual(100, health.GetReadonly(BlockingTargetEntityId).CurrentHealth, "A Dodgeable action must not affect a target currently holding DodgingComponent.");
    }

    [TestMethod]
    public void Apply_NonDodgeableAction_StillAffectsTargetCurrentlyDodging()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var dodgingEntities = new PackedComponentPool<DodgingComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        dodgingEntities.Add(BlockingTargetEntityId, new DodgingComponent(expiresAtFrame: 30));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, dodgingEntities: dodgingEntities);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth, "Only Dodgeable-tagged actions are affected by DodgingComponent -- everything else lands as normal.");
    }

    [TestMethod]
    public void Apply_DodgeableAction_TargetNotDodging_AffectsNormally()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var dodgingEntities = new PackedComponentPool<DodgingComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);

        TestActionEffects.Apply(DodgeableAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, dodgingEntities: dodgingEntities);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>The seam: a frozen target resolves nothing and cannot answer, so an action that reaches its tile passes over it.</summary>
    [TestMethod]
    public void Apply_FrozenTargetAtTargetTile_IsSkipped()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var tiers = new DirectComponentPool<ProcessingTierComponent>(16, static (ref existing, incoming) => existing = incoming);
        tiers.Add(BlockingTargetEntityId, new ProcessingTierComponent(ProcessingTierLevel.Borough));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0,
            processingTiers: new ProcessingTierQuery(tiers));

        Assert.AreEqual(100f, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    [DataRow(ProcessingTierLevel.Local)]
    [DataRow(ProcessingTierLevel.Neighborhood)]
    public void Apply_SimulatedTargetAtTargetTile_IsDamaged(ProcessingTierLevel tier)
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var tiers = new DirectComponentPool<ProcessingTierComponent>(16, static (ref existing, incoming) => existing = incoming);
        tiers.Add(BlockingTargetEntityId, new ProcessingTierComponent(tier));

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0,
            processingTiers: new ProcessingTierQuery(tiers));

        Assert.IsLessThan(100f, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    private static readonly ActionDefinition StaggeringAction = Action with { Id = Guid.NewGuid(), Tags = [Tag.Staggering, Tag.Dodgeable] };

    private static List<int> RecordStaggers(EventBus eventBus)
    {
        var staggered = new List<int>();
        eventBus.Subscribe<EntityStaggeredEvent>(e => staggered.Add(e.EntityId));
        return staggered;
    }

    [TestMethod]
    public void Apply_StaggeringAction_StaggersEachTargetItHits_ButNotItsSource()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, SourceEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        health.Add(SourceEntityId, new SimpleHealthComponent(100, 100));
        var staggered = RecordStaggers(eventBus);

        TestActionEffects.Apply(StaggeringAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        CollectionAssert.AreEqual(new[] { BlockingTargetEntityId }, staggered);
    }

    [TestMethod]
    public void Apply_StaggeringAction_AgainstADodgingTarget_DoesNotStagger()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var dodgingEntities = new PackedComponentPool<DodgingComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        dodgingEntities.Add(BlockingTargetEntityId, new DodgingComponent(expiresAtFrame: 30));
        var staggered = RecordStaggers(eventBus);

        TestActionEffects.Apply(StaggeringAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, dodgingEntities: dodgingEntities);

        Assert.IsEmpty(staggered);
    }

    [TestMethod]
    public void Apply_ActionWithoutTheStaggeringTag_DoesNotStagger()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var staggered = RecordStaggers(eventBus);

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0);

        Assert.IsEmpty(staggered);
    }

    [TestMethod]
    public void Apply_DamagingAction_PublishesFloatingTextAboveTheTargetOnly()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(SourceEntityId, new SimpleHealthComponent(100, 100));
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var floatingText = new TestFloatingText()
            .Place(SourceEntityId, ProcessingTierLevel.Local)
            .Place(BlockingTargetEntityId, ProcessingTierLevel.Local, TargetTile.X, TargetTile.Y);

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, floatingTextFeed: floatingText.Feed);

        var published = floatingText.Published.Single();
        Assert.AreEqual(BlockingTargetEntityId, published.EntityId);
        Assert.AreEqual(FloatingTextKind.DamageTaken, published.Kind);
    }

    [TestMethod]
    public void Apply_DodgeableActionOnADodgingTarget_PublishesDodgedAndNoDamage()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var dodgingEntities = new PackedComponentPool<DodgingComponent>(entityCapacity: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        dodgingEntities.Add(BlockingTargetEntityId, new DodgingComponent(expiresAtFrame: 30));
        var floatingText = new TestFloatingText().Place(BlockingTargetEntityId, ProcessingTierLevel.Local, TargetTile.X, TargetTile.Y);

        TestActionEffects.Apply(DodgeableAction, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, dodgingEntities: dodgingEntities, floatingTextFeed: floatingText.Feed);

        Assert.AreEqual(100f, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        var published = floatingText.Published.Single();
        Assert.AreEqual(FloatingTextKind.Dodged, published.Kind);
        Assert.AreEqual(BlockingTargetEntityId, published.EntityId);
    }

    [TestMethod]
    public void Apply_ShapeCoveringSeveralCellsOfOneTarget_ResolvesItOnce()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var secondTile = new Vector3Int(TargetTile.X + 1, TargetTile.Y, TargetTile.Z);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.SetBlockingOccupant(secondTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new SimpleHealthComponent(100, 100));
        var floatingText = new TestFloatingText().Place(BlockingTargetEntityId, ProcessingTierLevel.Local, TargetTile.X, TargetTile.Y, width: 2);

        TestActionEffects.Apply(Action, SourceEntityId, [TargetTile, secondTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, Keys, now: 0, floatingTextFeed: floatingText.Feed);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        Assert.HasCount(1, floatingText.Published);
    }
}
