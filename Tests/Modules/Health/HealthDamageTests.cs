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
    }

    private static PackedComponentPool<HealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_ReducesCurrentHealthByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));

        HealthDamage.Apply(pool, new EventBus(), 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)");

        Assert.AreEqual(40, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAtZero()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 5, healthRegen: 0, maximumHealth: 100));

        HealthDamage.Apply(pool, new EventBus(), 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)");

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        HealthDamage.Apply(pool, new EventBus(), 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)");
    }

    [TestMethod]
    public void Apply_PlayerEntity_PublishesEntityDamagedWithPostDamageHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDamaged? published = null;
        eventBus.Subscribe<EntityDamaged>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)");

        Assert.IsNotNull(published);
        Assert.AreEqual(10, published!.Value.Amount);
        Assert.AreEqual(40, published.Value.CurrentHealth);
        Assert.AreEqual(StatusEffectSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Burning)", published.Value.DamageType);
    }

    [TestMethod]
    public void Apply_NonPlayerEntity_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamaged>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Status Effect (Burning)");

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_PlayerIsSource_NonPlayerTarget_PublishesEntityDamagedWithTargetHealth()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDamaged? published = null;
        eventBus.Subscribe<EntityDamaged>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.FromEntity(0), new FakePlayerQuery(0), "Default Attack");

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.EntityId);
        Assert.AreEqual(40, published.Value.CurrentHealth);
        Assert.AreEqual(StatusEffectSource.FromEntity(0), published.Value.Source);
    }

    [TestMethod]
    public void Apply_NeitherPlayerNorPlayerSourced_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamaged>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.FromEntity(2), new FakePlayerQuery(0), "Contact");

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_NullPlayerQuery_DoesNotPublish()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamaged>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 0, 10, StatusEffectSource.Admin, null, "Status Effect (Burning)");

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_DamageBringsNonPlayerEntityToZero_PublishesEntityDiedWithSource()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 5, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityDied? published = null;
        eventBus.Subscribe<EntityDied>(e => published = e);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.FromEntity(0), new FakePlayerQuery(0), "Default Attack");
        eventBus.DispatchBuffered<EntityDied>();

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.EntityId);
        Assert.AreEqual(StatusEffectSource.FromEntity(0), published.Value.Source);
    }

    [TestMethod]
    public void Apply_DamageDoesNotReachZero_DoesNotPublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDied>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Contact");
        eventBus.DispatchBuffered<EntityDied>();

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_SecondHitAgainstAlreadyZeroEntity_DoesNotRepublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(1, new HealthComponent(currentHealth: 5, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var publishCount = 0;
        eventBus.Subscribe<EntityDied>(_ => publishCount++);

        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Contact");
        HealthDamage.Apply(pool, eventBus, 1, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Contact");
        eventBus.DispatchBuffered<EntityDied>();

        Assert.AreEqual(1, publishCount);
    }

    [TestMethod]
    public void Apply_PlayerEntityAtExactlyZero_DoesNotPublishEntityDied()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 5, healthRegen: 0, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDied>(_ => published = true);

        HealthDamage.Apply(pool, eventBus, 0, 10, StatusEffectSource.Admin, new FakePlayerQuery(0), "Contact");
        eventBus.DispatchBuffered<EntityDied>();

        Assert.IsFalse(published);
    }
}
