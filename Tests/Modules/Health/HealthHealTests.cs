using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthHealTests
{
    private static PackedComponentPool<SimpleHealthComponent> CreatePool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_RaisesCurrentHealthByFractionOfMaximum()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        TestHealth.Heal(pool, 0, 0.1f, now: 0);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 95, maximumHealth: 100));

        TestHealth.Heal(pool, 0, 0.5f, now: 0);

        Assert.AreEqual(100, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        TestHealth.Heal(pool, 0, 0.1f, now: 0);
    }

    [TestMethod]
    public void Apply_ComplexTarget_RoutesThroughComplexHealthHeal()
    {
        var pool = CreatePool();
        var bodyParts = BodyPartTestWorld.WithParts(0, ("Torso", BodyPartType.Torso, 50, 100, true)).BodyParts;

        TestHealth.Heal(pool, 0, 0.25f, bodyParts: bodyParts, now: 0);

        bodyParts.TryGet(0, 0, out var torso);
        Assert.AreEqual(75, torso.CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponentOrBodyParts_DoesNotThrow()
    {
        var pool = CreatePool();
        var bodyParts = new BodyPartTestWorld().BodyParts;

        TestHealth.Heal(pool, 0, 0.1f, bodyParts: bodyParts, now: 0);
    }

    [TestMethod]
    public void Apply_PlayerIsTarget_PublishesEntityHealedEvent()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityHealedEvent? published = null;
        eventBus.Subscribe<EntityHealedEvent>(e => published = e);

        TestHealth.Heal(pool, 0, 0.1f, eventBus: eventBus, playerQuery: new TestPlayerQuery(0), now: 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(10f, published.Value.Amount);
        Assert.AreEqual(60f, published.Value.CurrentHealth);
        Assert.AreEqual(100f, published.Value.MaximumHealth);
    }

    [TestMethod]
    public void Apply_PlayerNotInvolved_DoesNotPublishEntityHealedEvent()
    {
        var pool = CreatePool();
        pool.Add(1, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityHealedEvent>(_ => published = true);

        TestHealth.Heal(pool, 1, 0.1f, sourceEntityId: 2, eventBus: eventBus, playerQuery: new TestPlayerQuery(0), now: 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_NoEventBusOrPlayerQuery_DoesNotThrow()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        TestHealth.Heal(pool, 0, 0.1f, now: 0);

        Assert.AreEqual(60f, pool.GetReadonly(0).CurrentHealth);
    }
}
