using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthDamageTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
        public Engine.ECS.Entities.EntityKey PlayerEntityKey { get; init; } = TestSources.KeyOf(playerEntityId);
    }

    private static PackedComponentPool<SimpleHealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_ReducesCurrentHealthByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        HealthDamage.Apply(pool, new EventBus(), 0, 10, ActionSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)", now: 0);

        Assert.AreEqual(40, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAtZero()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 5, maximumHealth: 100));

        HealthDamage.Apply(pool, new EventBus(), 0, 10, ActionSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)", now: 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        HealthDamage.Apply(pool, new EventBus(), 0, 10, ActionSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)", now: 0);
    }

    [TestMethod]
    public void Apply_PlayerEntity_PublishesEntityDamagedWithPostDamageHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 0, 10, ActionSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)", now: 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(10, published!.Value.Amount);
        Assert.AreEqual(40, published.Value.CurrentHealth);
        Assert.AreEqual(ActionSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Burning)", published.Value.DamageType);
    }

    [TestMethod]
    public void Apply_NonPlayerEntity_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, ActionSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)", now: 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_PlayerIsSource_NonPlayerTarget_PublishesEntityDamagedWithTargetHealth()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 1, 10, TestSources.Entity(0), new FakePlayerQuery(0), "Default Attack", now: 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.EntityId);
        Assert.AreEqual(40, published.Value.CurrentHealth);
        Assert.AreEqual(TestSources.Entity(0), published.Value.Source);
    }

    [TestMethod]
    public void Apply_NeitherPlayerNorPlayerSourced_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, TestSources.Entity(2), new FakePlayerQuery(0), "Contact", now: 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_NullPlayerQuery_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 0, 10, ActionSource.Admin, null, "Status Effect (Burning)", now: 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_DamageBringsNonPlayerEntityToZero_PublishesEntityDiedWithSource()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 5, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDiedEvent? published = null;
        eventBus.Subscribe<EntityDiedEvent>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 1, 10, TestSources.Entity(0), new FakePlayerQuery(0), "Default Attack", now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.EntityId);
        Assert.AreEqual(TestSources.Entity(0), published.Value.Source);
    }

    [TestMethod]
    public void Apply_DamageDoesNotReachZero_DoesNotPublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDiedEvent>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, ActionSource.Admin, new FakePlayerQuery(0), "Contact", now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_SecondHitAgainstAlreadyZeroEntity_DoesNotRepublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 5, maximumHealth: 100));
        var eventBus = new EventBus();
        var publishCount = 0;
        eventBus.Subscribe<EntityDiedEvent>(_ => publishCount++);

        HealthDamage.Apply(pool, eventBus, 1, 10, ActionSource.Admin, new FakePlayerQuery(0), "Contact", now: 0);
        HealthDamage.Apply(pool, eventBus, 1, 10, ActionSource.Admin, new FakePlayerQuery(0), "Contact", now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.AreEqual(1, publishCount);
    }

    [TestMethod]
    public void Apply_PlayerEntityAtExactlyZero_DoesNotPublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 5, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDiedEvent>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 0, 10, ActionSource.Admin, new FakePlayerQuery(0), "Contact", now: 0);
        eventBus.DispatchBuffered<EntityDiedEvent>();

        Assert.IsFalse(published);
    }
}
