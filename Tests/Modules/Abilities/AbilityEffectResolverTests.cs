using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class AbilityEffectResolverTests
{
    private const int SourceEntityId = 1;
    private const int BlockingTargetEntityId = 2;
    private const int NonBlockingTargetEntityId = 3;
    private const int SecondNonBlockingTargetEntityId = 4;
    private static readonly Vector3Int TargetTile = new(5, 5, 0);
    private static readonly AbilityDefinition Ability = new(
        Guid.NewGuid(),
        "Test Attack",
        "#",
        new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
        new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null),
        new AbilityEffect(DamageAmount: 0, StatusEffects: []));
    private static readonly AbilityInstanceComponent Instance = new(Ability.Id, damageAmount: 15, cooldownFramesRemaining: 0);

    /// <summary>Records every ApplyStack call it receives instead of touching any real component pool -- keeps these tests independent of any concrete effect (Burning/Poison/Paralysis).</summary>
    private sealed class FakeStatusEffectAuraApplier(StatusEffectType effectType) : IStatusEffectAuraApplier
    {
        public StatusEffectType EffectType { get; } = effectType;
        public List<(int EntityId, StatusEffectSource Source)> AppliedCalls { get; } = [];

        public int GetCurrentStackCount(ComponentManager componentManager, int entityId) => AppliedCalls.Count(call => call.EntityId == entityId);

        public void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source) => AppliedCalls.Add((entityId, source));
    }

    /// <summary>Minimal IMapQuery test double supporting both the Blocking slot and the non-Blocking index -- everything else is unused by AbilityEffectResolver.Apply.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blockingByPosition = [];
        private readonly Dictionary<Vector3Int, List<int>> _nonBlockingByPosition = [];

        public Vector3Int MapSize { get; } = new(100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }

        public void SetBlockingOccupant(Vector3Int position, int entityId) => _blockingByPosition[position] = entityId;

        public void AddNonBlockingOccupant(Vector3Int position, int entityId)
        {
            if (!_nonBlockingByPosition.TryGetValue(position, out var entityIds))
            {
                entityIds = [];
                _nonBlockingByPosition[position] = entityIds;
            }

            entityIds.Add(entityId);
        }

        public int GetEntityIdAt(Vector3Int position) => _blockingByPosition.TryGetValue(position, out var id) ? id : -1;

        public IReadOnlyList<int> GetNonBlockingEntityIdsAt(Vector3Int position) =>
            _nonBlockingByPosition.TryGetValue(position, out var entityIds) ? entityIds : [];
    }

    private static (FakeMapQuery MapQuery, PackedComponentPool<HealthComponent> Health, EventBus EventBus, StatusEffectAuraApplierRegistry StatusEffectAppliers, ComponentManager ComponentManager) Build()
    {
        var mapQuery = new FakeMapQuery();
        var health = new PackedComponentPool<HealthComponent>(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        var eventBus = new EventBus();
        var statusEffectAppliers = new StatusEffectAuraApplierRegistry();
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);

        return (mapQuery, health, eventBus, statusEffectAppliers, componentManager);
    }

    [TestMethod]
    public void Apply_BlockingOccupantAtTargetTile_DamagesIt()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>The requirement this test guards: a tile-targeted ability must hit Tiny/Phasing entities too, not just the single Blocking occupant Map's own array can answer for.</summary>
    [TestMethod]
    public void Apply_NonBlockingEntityAtTargetTile_DamagesItEvenWithNoBlockingOccupant()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>Stacked non-Blocking entities (e.g. several Tiny goblins sharing a cell) must all be hit by the same activation, not just the first.</summary>
    [TestMethod]
    public void Apply_MultipleNonBlockingEntitiesStackedAtTargetTile_DamagesAllOfThem()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, SecondNonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));
        health.Add(SecondNonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(SecondNonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>A Blocking occupant and a Phasing entity can legitimately overlap the same tile -- both must be damaged by one activation, not just one or the other.</summary>
    [TestMethod]
    public void Apply_BlockingOccupantOverlappingNonBlockingEntity_DamagesBoth()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 0, 100));
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoOccupantsAtTargetTile_DoesNothing()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
    }

    private static readonly AbilityDefinition AbilityWithStatusEffect = new(
        Guid.NewGuid(),
        "Test Status Effect Attack",
        "#",
        new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
        new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null),
        new AbilityEffect(DamageAmount: 0, StatusEffects: [StatusEffectType.Paralysis]));
    private static readonly AbilityInstanceComponent StatusEffectInstance = new(AbilityWithStatusEffect.Id, damageAmount: 0, cooldownFramesRemaining: 0);

    [TestMethod]
    public void Apply_BlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(1, applier.AppliedCalls.Count);
        Assert.AreEqual(BlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
        Assert.AreEqual(StatusEffectSource.FromEntity(SourceEntityId), applier.AppliedCalls[0].Source);
    }

    [TestMethod]
    public void Apply_NonBlockingOccupant_GrantsRegisteredStatusEffect()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.AreEqual(1, applier.AppliedCalls.Count);
        Assert.AreEqual(NonBlockingTargetEntityId, applier.AppliedCalls[0].EntityId);
    }

    /// <summary>The concrete "immortal but affectable" regression: the target never gets a HealthComponent at all, and the status effect still grants.</summary>
    [TestMethod]
    public void Apply_TargetWithNoHealthComponentAtAll_StillGrantsStatusEffect()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        var applier = new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis);
        statusEffectAppliers.Register(applier);
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
        Assert.AreEqual(1, applier.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotThrow()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);
    }

    [TestMethod]
    public void Apply_StatusEffectGranted_PublishesStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        statusEffectAppliers.Register(new FakeStatusEffectAuraApplier(StatusEffectType.Paralysis));
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        StatusEffectApplied? published = null;
        eventBus.Subscribe<StatusEffectApplied>(e => published = e);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsNotNull(published);
        Assert.AreEqual(BlockingTargetEntityId, published!.Value.EntityId);
        Assert.AreEqual(StatusEffectType.Paralysis, published.Value.EffectType);
        Assert.AreEqual(StatusEffectSource.FromEntity(SourceEntityId), published.Value.Source);
    }

    [TestMethod]
    public void Apply_StatusEffectTypeWithNoRegisteredApplier_DoesNotPublishStatusEffectApplied()
    {
        var (mapQuery, health, eventBus, statusEffectAppliers, componentManager) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        var published = false;
        eventBus.Subscribe<StatusEffectApplied>(_ => published = true);

        AbilityEffectResolver.Apply(AbilityWithStatusEffect, StatusEffectInstance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null, statusEffectAppliers, componentManager);

        Assert.IsFalse(published);
    }
}
