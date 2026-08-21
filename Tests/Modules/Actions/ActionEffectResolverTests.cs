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
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActionEffectResolverTests
{
    private const int SourceEntityId = 1;
    private const int BlockingTargetEntityId = 2;
    private const int NonBlockingTargetEntityId = 3;
    private const int SecondNonBlockingTargetEntityId = 4;
    private static readonly Vector3Int TargetTile = new(5, 5, 0);
    private static readonly ActionDefinition Action = new(
        Guid.NewGuid(), "Test Attack", null, "#", default, [],
        Effects: [new ActionEffect([new DirectDamage(MinAmount: 0, MaxAmount: 0)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));
    private static readonly ActionInstanceComponent Instance = new(Action.Id, damageAmount: 15, cooldownFramesRemaining: 0);

    /// <summary>Records every ApplyStack call it receives instead of touching any real component pool -- keeps these tests independent of any concrete effect (Burning/Poison/Paralysis).</summary>
    private sealed class FakeStatusEffectAuraApplier(StatusEffectType effectType) : IStatusEffectAuraApplier
    {
        public StatusEffectType EffectType { get; } = effectType;
        public List<(int EntityId, StatusEffectSource Source)> AppliedCalls { get; } = [];

        public int GetCurrentStackCount(ComponentManager componentManager, int entityId) => AppliedCalls.Count(call => call.EntityId == entityId);

        public void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source) => AppliedCalls.Add((entityId, source));
    }

    /// <summary>Never rolls a crit -- NextDouble always returns 1.0, comfortably above any crit chance -- so damage-amount assertions in these orchestration tests stay deterministic.</summary>
    private sealed class NeverCritRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

    /// <summary>Minimal IMapQuery test double supporting both the Blocking slot and the general occupant index -- everything else is unused by ActionEffectResolver.Apply.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blockingByPosition = [];
        private readonly Dictionary<Vector3Int, List<int>> _occupantsByPosition = [];

        public Vector3Int MapSize { get; } = new(100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
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

    private static (FakeMapQuery MapQuery, PackedComponentPool<HealthComponent> Health, EventBus EventBus, MathUtility MathUtility, StatusEffectAuraApplierRegistry StatusEffectAppliers, ComponentManager ComponentManager) Build()
    {
        var mapQuery = new FakeMapQuery();
        var health = new PackedComponentPool<HealthComponent>(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        var eventBus = new EventBus();
        var mathUtility = new MathUtility(new NeverCritRandom());
        var statusEffectAppliers = new StatusEffectAuraApplierRegistry();
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);

        return (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager);
    }

    [TestMethod]
    public void Apply_BlockingOccupantAtTargetTile_DamagesIt()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 100));

        ActionEffectResolver.Apply(Action, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>The requirement this test guards: a tile-targeted action must hit Tiny/Phasing entities too, not just the single Blocking occupant Map's own array can answer for.</summary>
    [TestMethod]
    public void Apply_NonBlockingEntityAtTargetTile_DamagesItEvenWithNoBlockingOccupant()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 100));

        ActionEffectResolver.Apply(Action, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>Stacked non-Blocking entities (e.g. several Tiny goblins sharing a cell) must all be hit by the same activation, not just the first.</summary>
    [TestMethod]
    public void Apply_MultipleNonBlockingEntitiesStackedAtTargetTile_DamagesAllOfThem()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, SecondNonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 100));
        health.Add(SecondNonBlockingTargetEntityId, new HealthComponent(100, 100));

        ActionEffectResolver.Apply(Action, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(SecondNonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>A Blocking occupant and a Phasing entity can legitimately overlap the same tile -- both must be damaged by one activation, not just one or the other.</summary>
    [TestMethod]
    public void Apply_BlockingOccupantOverlappingNonBlockingEntity_DamagesBoth()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 100));
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 100));

        ActionEffectResolver.Apply(Action, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoOccupantsAtTargetTile_DoesNothing()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();

        ActionEffectResolver.Apply(Action, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
    }

    private static readonly ActionDefinition StrengthTaggedAction = new(
        Guid.NewGuid(), "Test Strength Attack", null, "#", default, [Tag.Strength],
        Effects: [new ActionEffect([new DirectDamage(MinAmount: 0, MaxAmount: 0)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));
    private static readonly ActionInstanceComponent StrengthTaggedInstance = new(StrengthTaggedAction.Id, damageAmount: 15, cooldownFramesRemaining: 0);

    [TestMethod]
    public void Apply_ActionTaggedWithMatchingAbilityScore_AddsScoreTotalToBaseDamage()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 100));
        componentManager.RegisterMultiPool<AbilityScoreComponent>();
        var abilityScores = componentManager.GetMultiPool<AbilityScoreComponent>();
        abilityScores.Add(SourceEntityId, new AbilityScoreComponent(AbilityScoreType.Strength, baseValue: 8, total: 8));

        ActionEffectResolver.Apply(StrengthTaggedAction, StrengthTaggedInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, statModifiers: null, deadEntities: null, abilityScores: abilityScores);

        // 15 base damage + 8 Strength Total = 23.
        Assert.AreEqual(77, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NullAbilityScoresPool_BehavesExactlyAsToday_NoBonusNoCrash()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 100));

        ActionEffectResolver.Apply(StrengthTaggedAction, StrengthTaggedInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_AbilityScoresPoolPresentButSourceHasNoMatchingScore_NoBonus()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 100));
        componentManager.RegisterMultiPool<AbilityScoreComponent>();
        var abilityScores = componentManager.GetMultiPool<AbilityScoreComponent>();
        // SourceEntityId has no AbilityScoreComponent entries at all.

        ActionEffectResolver.Apply(StrengthTaggedAction, StrengthTaggedInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, statModifiers: null, deadEntities: null, abilityScores: abilityScores);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    private static readonly ActionDefinition ActionWithStatusEffect = new(
        Guid.NewGuid(), "Test Status Effect Attack", null, "#", default, [],
        Effects: [new ActionEffect([new StatusEffectGrant(StatusEffectType.Paralysis)])],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null)));
    private static readonly ActionInstanceComponent StatusEffectInstance = new(ActionWithStatusEffect.Id, damageAmount: 0, cooldownFramesRemaining: 0);

    [TestMethod]
    public void Apply_BlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(1, applier.AppliedCalls.Count);
        Assert.AreEqual(BlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
        Assert.AreEqual(StatusEffectSource.FromEntity(SourceEntityId), applier.AppliedCalls[0].Source);
    }

    [TestMethod]
    public void Apply_NonBlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(1, applier.AppliedCalls.Count);
        Assert.AreEqual(NonBlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
    }

    /// <summary>The concrete "immortal but affectable" regression: the target never gets a HealthComponent at all, and the status effect still grants.</summary>
    [TestMethod]
    public void Apply_TargetWithNoHealthComponentAtAll_StillGrantsStatusEffect()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
        Assert.AreEqual(1, applier.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotThrow()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);
    }

    [TestMethod]
    public void Apply_StatusEffectGranted_PublishesStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        statusEffectAppliers.Register(new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis));
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        StatusEffectAppliedEvent? published = null;
        eventBus.Subscribe<StatusEffectAppliedEvent>(e => published = e);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsNotNull(published);
        Assert.AreEqual(BlockingTargetEntityId, published!.Value.EntityId);
        Assert.AreEqual(StatusEffectType.Paralysis, published.Value.EffectType);
        Assert.AreEqual(StatusEffectSource.FromEntity(SourceEntityId), published.Value.Source);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotPublishStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, mathUtility, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        var published = false;
        eventBus.Subscribe<StatusEffectAppliedEvent>(_ => published = true);

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager);

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
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.GetPackedPool<DeadComponent>().Add(BlockingTargetEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        ActionEffectResolver.Apply(ActionWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, mathUtility, playerQuery: null, statusEffectAppliers, componentManager, statModifiers: null, componentManager.GetPackedPool<DeadComponent>());

        Assert.AreEqual(0, applier.AppliedCalls.Count);
    }
}
