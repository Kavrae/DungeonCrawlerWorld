using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Health.Components;
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

    private static (FakeMapQuery MapQuery, PackedComponentPool<HealthComponent> Health, EventBus EventBus) Build()
    {
        var mapQuery = new FakeMapQuery();
        var health = new PackedComponentPool<HealthComponent>(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        var eventBus = new EventBus();

        return (mapQuery, health, eventBus);
    }

    [TestMethod]
    public void Apply_BlockingOccupantAtTargetTile_DamagesIt()
    {
        var (mapQuery, health, eventBus) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>The requirement this test guards: a tile-targeted ability must hit Tiny/Phasing entities too, not just the single Blocking occupant Map's own array can answer for.</summary>
    [TestMethod]
    public void Apply_NonBlockingEntityAtTargetTile_DamagesItEvenWithNoBlockingOccupant()
    {
        var (mapQuery, health, eventBus) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>Stacked non-Blocking entities (e.g. several Tiny goblins sharing a cell) must all be hit by the same activation, not just the first.</summary>
    [TestMethod]
    public void Apply_MultipleNonBlockingEntitiesStackedAtTargetTile_DamagesAllOfThem()
    {
        var (mapQuery, health, eventBus) = Build();
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, SecondNonBlockingTargetEntityId);
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));
        health.Add(SecondNonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null);

        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(SecondNonBlockingTargetEntityId).CurrentHealth);
    }

    /// <summary>A Blocking occupant and a Phasing entity can legitimately overlap the same tile -- both must be damaged by one activation, not just one or the other.</summary>
    [TestMethod]
    public void Apply_BlockingOccupantOverlappingNonBlockingEntity_DamagesBoth()
    {
        var (mapQuery, health, eventBus) = Build();
        mapQuery.SetBlockingOccupant(TargetTile, BlockingTargetEntityId);
        mapQuery.AddNonBlockingOccupant(TargetTile, NonBlockingTargetEntityId);
        health.Add(BlockingTargetEntityId, new HealthComponent(100, 0, 100));
        health.Add(NonBlockingTargetEntityId, new HealthComponent(100, 0, 100));

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null);

        Assert.AreEqual(85, health.GetReadonly(BlockingTargetEntityId).CurrentHealth);
        Assert.AreEqual(85, health.GetReadonly(NonBlockingTargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoOccupantsAtTargetTile_DoesNothing()
    {
        var (mapQuery, health, eventBus) = Build();

        AbilityEffectResolver.Apply(Ability, Instance, SourceEntityId, [TargetTile], mapQuery, health, eventBus, playerQuery: null);

        Assert.IsFalse(health.Has(BlockingTargetEntityId));
    }
}
