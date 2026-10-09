using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Auras.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Death;

[TestClass]
public sealed class DeathSystemTests
{
    /// <summary>Records ConvertToNonBlocking calls instead of touching any real World -- pairs with a plain TransformComponent pool so this test needs no Game.World.World in the object graph.</summary>
    private sealed class RecordingEntityMoveSync : IEntityMoveSync
    {
        public int? LastConvertedEntityId { get; private set; }
        public int ConvertToNonBlockingCallCount { get; private set; }
        public void SyncMove(EntityMovedEvent moved, bool isBlocking) { }
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

        public MapBounds Bounds { get; } = new(0, 0, 100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public ReadOnlySpan<int> GetOccupantEntityIdSpanAt(Vector3Int position) => [];
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }

        public void SetBlocking(int entityId) => _blockingEntityIds.Add(entityId);

        public bool IsBlocking(int entityId) => _blockingEntityIds.Contains(entityId);
    }

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<NonBlockingComponent> CreateNonBlockingPool() =>
        new(entityCapacity: 10, initialCapacity: 4);

    private static DirectComponentPool<TransformComponent> CreateTransformPool()
    {
        var pool = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        pool.Add(0, new TransformComponent(new Vector3Int(1, 1, 0), new Vector2Byte(1, 1)));
        return pool;
    }

    private static MultiComponentPool<AuraSourceComponent> CreateAuraSourcePool() =>
        new(entityCapacity: 10, initialCapacity: 4);

    private static (DeathSystem System, PackedComponentPool<DeadComponent> DeadEntities, MultiComponentPool<NonBlockingComponent> NonBlockingEntities, RecordingEntityMoveSync EntityMoveSync, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var deadEntities = CreateDeadPool();
        var nonBlockingEntities = CreateNonBlockingPool();
        var transforms = CreateTransformPool();
        var entityMoveSync = new RecordingEntityMoveSync();
        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        var system = TestSystems.DeathSystem(deadEntities, nonBlockingEntities, transforms, entityMoveSync, mapQuery, eventBus);

        return (system, deadEntities, nonBlockingEntities, entityMoveSync, mapQuery, eventBus);
    }

    [TestMethod]
    public void EntityDied_WasBlocking_ConvertsToNonBlocking()
    {
        var (_, _, _, entityMoveSync, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(0, entityMoveSync.LastConvertedEntityId);
    }

    [TestMethod]
    public void EntityDied_WasBlocking_AddsNonBlockingComponent()
    {
        var (_, _, nonBlockingEntities, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(nonBlockingEntities.Has(0));
    }

    /// <summary>The concrete regression this whole design change protects: an already-non-Blocking entity (e.g. a Phasing Ghost) may share its tile with a real Blocking occupant, so its death must never touch Map's Blocking slot -- see World.ConvertToNonBlocking's own doc comment.</summary>
    [TestMethod]
    public void EntityDied_WasAlreadyNonBlocking_DoesNotConvert()
    {
        var (_, _, nonBlockingEntities, entityMoveSync, _, eventBus) = Build(); // FakeMapQuery.IsBlocking defaults to false.

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(0, entityMoveSync.ConvertToNonBlockingCallCount);
        Assert.IsFalse(nonBlockingEntities.Has(0));
    }

    [TestMethod]
    public void EntityDied_AddsDeadComponentRecordingTheKillersKey()
    {
        var (_, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(deadEntities.Has(0));
        Assert.AreEqual(TestSources.KeyOf(1), deadEntities.GetReadonly(0).KilledBy.Key);
    }

    [TestMethod]
    public void EntityDied_AdminSource_AddsDeadComponentKilledByAdmin()
    {
        var (_, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, ActionSource.Admin));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(deadEntities.Has(0));
        Assert.AreEqual(ActionSource.Admin, deadEntities.GetReadonly(0).KilledBy);
    }

    [TestMethod]
    public void EntityDied_AlreadyDead_DoesNotConvertAgain()
    {
        var (_, _, _, entityMoveSync, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();
        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(2)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, entityMoveSync.ConvertToNonBlockingCallCount);
    }

    [TestMethod]
    public void Update_DispatchesQueuedEntityDied()
    {
        var (system, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        system.Update(default, 0);

        Assert.IsTrue(deadEntities.Has(0));
    }

    [TestMethod]
    public void Update_StampsDeadComponentWithCurrentFrameCount()
    {
        var (system, deadEntities, _, _, mapQuery, eventBus) = Build();
        mapQuery.SetBlocking(0);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        system.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, false, FrameCount: 12345), 0);

        Assert.AreEqual(12345, deadEntities.GetReadonly(0).DiedAtFrame);
    }

    /// <summary>A corpse persists indefinitely, so a source no toggle holds (a blueprint's, a timed grant's) is retracted at death rather than radiating from it for good.</summary>
    [TestMethod]
    public void EntityDied_HasActiveAuraSource_RetractsItAndPublishesRemoved()
    {
        var deadEntities = CreateDeadPool();
        var nonBlockingEntities = CreateNonBlockingPool();
        var transforms = CreateTransformPool();
        var entityMoveSync = new RecordingEntityMoveSync();
        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var auraSources = CreateAuraSourcePool();
        var source = new AuraSourceComponent(TestAuras.PoisonId, power: 5, size: 2);
        auraSources.Add(0, source);

        var system = TestSystems.DeathSystem(deadEntities, nonBlockingEntities, transforms, entityMoveSync, mapQuery, eventBus, auraSources);

        AuraSourceRemovedEvent? published = null;
        eventBus.Subscribe<AuraSourceRemovedEvent>(e => published = e);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsFalse(auraSources.Has(0));
        Assert.IsNotNull(published);
        Assert.AreEqual(0, published!.Value.EntityId);
        Assert.AreEqual(source, published.Value.Source);
    }

    /// <summary>A source a toggle holds is the toggle's to end: a lit item keeps working on its holder's corpse.</summary>
    [TestMethod]
    public void EntityDied_HeldAuraSource_IsLeftInPlace()
    {
        var deadEntities = CreateDeadPool();
        var eventBus = new EventBus();
        var auraSources = CreateAuraSourcePool();
        auraSources.Add(0, new AuraSourceComponent(TestAuras.PoisonId, power: 5, size: 2));
        auraSources.Add(0, new AuraSourceComponent(TestAuras.PoisonId, power: 16, size: 4, heldGrantKey: 4));

        TestSystems.DeathSystem(deadEntities, CreateNonBlockingPool(), CreateTransformPool(), new RecordingEntityMoveSync(), new FakeMapQuery(), eventBus, auraSources);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, auraSources.CountForEntity(0));
        Assert.AreEqual(4u, auraSources.GetReadonlyByDenseIndex(auraSources.GetFirstDenseIndex(0)).HeldGrantKey);
    }

    private static (PackedComponentPool<DeadComponent> DeadEntities, DamageLedger Ledger, MultiComponentPool<DamageContributionComponent> Contributions, EventBus EventBus) BuildWithLedger()
    {
        var deadEntities = CreateDeadPool();
        var contributions = EmptyPools.Multi<DamageContributionComponent>();
        var ledger = new DamageLedger(contributions, EmptyPools.Packed<DamageLedgerExpiryComponent>(), deadEntities, new EntityKeys());
        var eventBus = new EventBus();
        TestSystems.DeathSystem(deadEntities, CreateNonBlockingPool(), CreateTransformPool(), new RecordingEntityMoveSync(), new FakeMapQuery(), eventBus, damageLedger: ledger);
        return (deadEntities, ledger, contributions, eventBus);
    }

    [TestMethod]
    public void EntityDied_TheTopDamageDealerOwnsTheLoot_AndTheLedgerIsCleared()
    {
        var (deadEntities, ledger, contributions, eventBus) = BuildWithLedger();
        ledger.Record(0, TestSources.Entity(1), 5, now: 0);
        ledger.Record(0, TestSources.Entity(2), 9, now: 1);

        eventBus.Publish(new EntityDiedEvent(0, TestSources.Entity(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(TestSources.KeyOf(2), deadEntities.GetReadonly(0).LootOwnerEntityKey);
        Assert.IsFalse(contributions.Has(0));
    }

    [TestMethod]
    public void EntityDied_NoEntityDamagedIt_NobodyOwnsTheLoot()
    {
        var (deadEntities, _, _, eventBus) = BuildWithLedger();

        eventBus.Publish(new EntityDiedEvent(0, ActionSource.FromTerrain(1)));
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsTrue(deadEntities.GetReadonly(0).LootOwnerEntityKey.IsNone);
    }
}
