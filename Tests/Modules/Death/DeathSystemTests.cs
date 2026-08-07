using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.World;

namespace Tests.Modules.Death;

[TestClass]
public sealed class DeathSystemTests
{
    /// <summary>Records ConvertToNonBlocking calls instead of touching any real World -- pairs with a plain TransformComponent pool so this test needs no Game.World.World in the object graph.</summary>
    private sealed class RecordingEntityMoveSync : IEntityMoveSync
    {
        public int? LastConvertedEntityId { get; private set; }
        public int ConvertToNonBlockingCallCount { get; private set; }
        public void SyncMove(EntityMovedEvent moved) { }
        public void ConvertToNonBlocking(int entityId, ref TransformComponent transform)
        {
            LastConvertedEntityId = entityId;
            ConvertToNonBlockingCallCount++;
        }
    }

    /// <summary>Minimal IMapQuery test double with a settable per-entity IsBlocking answer -- the only member DeathSystem actually reads.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly HashSet<int> _blockingEntityIds = [];

        public Vector3Int MapSize { get; } = new(100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }

        public void SetBlocking(int entityId) => _blockingEntityIds.Add(entityId);

        public bool IsBlocking(int entityId) => _blockingEntityIds.Contains(entityId);
    }

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<NonBlockingComponent> CreateNonBlockingPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4);

    private static DirectComponentPool<TransformComponent> CreateTransformPool()
    {
        var pool = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        pool.Add(0, new TransformComponent(new Vector3Int(1, 1, 0), new Vector2Byte(1, 1)));
        return pool;
    }

    private static (DeathSystem System, PackedComponentPool<DeadComponent> DeadEntities, MultiComponentPool<NonBlockingComponent> NonBlockingEntities, RecordingEntityMoveSync EntityMoveSync, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var deadEntities = CreateDeadPool();
        var nonBlockingEntities = CreateNonBlockingPool();
        var transforms = CreateTransformPool();
        var entityMoveSync = new RecordingEntityMoveSync();
        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        var system = new DeathSystem(deadEntities, nonBlockingEntities, transforms, entityMoveSync, mapQuery, eventBus);

        return (system, deadEntities, nonBlockingEntities, entityMoveSync, mapQuery, eventBus);
    }

    [TestMethod]
    public void EntityDied_WasBlocking_ConvertsToNonBlocking()
    {
        var (_, _, _, entityMoveSync, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(0, entityMoveSync.LastConvertedEntityId);
    }

    [TestMethod]
    public void EntityDied_WasBlocking_AddsNonBlockingComponent()
    {
        var (_, _, nonBlockingEntities, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(nonBlockingEntities.Has(0));
    }

    /// <summary>The concrete regression this whole design change protects: an already-non-Blocking entity (e.g. a Phasing Ghost) may share its tile with a real Blocking occupant, so its death must never touch Map's Blocking slot -- see World.ConvertToNonBlocking's own doc comment.</summary>
    [TestMethod]
    public void EntityDied_WasAlreadyNonBlocking_DoesNotConvert()
    {
        var (_, _, nonBlockingEntities, entityMoveSync, _, eventBus) = Build(); // FakeMapQuery.IsBlocking defaults to false.

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(0, entityMoveSync.ConvertToNonBlockingCallCount);
        Assert.IsFalse(nonBlockingEntities.Has(0));
    }

    [TestMethod]
    public void EntityDied_AddsDeadComponentWithKilledByEntityId()
    {
        var (_, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(deadEntities.Has(0));
        Assert.AreEqual(1, deadEntities.GetReadonly(0).KilledByEntityId);
    }

    [TestMethod]
    public void EntityDied_AdminSource_AddsDeadComponentWithNullKilledByEntityId()
    {
        var (_, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.Admin));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(deadEntities.Has(0));
        Assert.IsNull(deadEntities.GetReadonly(0).KilledByEntityId);
    }

    [TestMethod]
    public void EntityDied_AlreadyDead_DoesNotConvertAgain()
    {
        var (_, _, _, entityMoveSync, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();
        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(2)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, entityMoveSync.ConvertToNonBlockingCallCount);
    }

    [TestMethod]
    public void Update_DispatchesQueuedEntityDied()
    {
        var (system, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, StatusEffectSource.FromEntity(1)));
        system.Update(default, 0);

        Assert.IsTrue(deadEntities.Has(0));
    }
}
