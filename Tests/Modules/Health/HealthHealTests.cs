using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthHealTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static PackedComponentPool<SimpleHealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_RaisesCurrentHealthByFractionOfMaximum()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        HealthHeal.Apply(pool, 0, 0.1f);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 95, maximumHealth: 100));

        HealthHeal.Apply(pool, 0, 0.5f);

        Assert.AreEqual(100, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        HealthHeal.Apply(pool, 0, 0.1f);
    }

    [TestMethod]
    public void Apply_ComplexTarget_RoutesThroughComplexHealthHeal()
    {
        var pool = CreatePool();
        var bodyParts = new MultiComponentPool<BodyPartComponent>(maximumEntityCount: 10, initialCapacity: 4);
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 100, isVital: true));

        HealthHeal.Apply(pool, 0, 0.25f, bodyParts: bodyParts);

        Assert.AreEqual(75, bodyParts.GetReadonlyByDenseIndex(bodyParts.GetFirstDenseIndex(0)).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponentOrBodyParts_DoesNotThrow()
    {
        var pool = CreatePool();
        var bodyParts = new MultiComponentPool<BodyPartComponent>(maximumEntityCount: 10, initialCapacity: 4);

        HealthHeal.Apply(pool, 0, 0.1f, bodyParts: bodyParts);
    }

    [TestMethod]
    public void Apply_PlayerIsTarget_PublishesEntityHealedEvent()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var eventBus = new EventBus();
        EntityHealedEvent? published = null;
        eventBus.Subscribe<EntityHealedEvent>(e => published = e);

        HealthHeal.Apply(pool, 0, 0.1f, eventBus: eventBus, playerQuery: new FakePlayerQuery(0));

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

        HealthHeal.Apply(pool, 1, 0.1f, sourceEntityId: 2, eventBus: eventBus, playerQuery: new FakePlayerQuery(0));

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Apply_NoEventBusOrPlayerQuery_DoesNotThrow()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));

        HealthHeal.Apply(pool, 0, 0.1f);

        Assert.AreEqual(60f, pool.GetReadonly(0).CurrentHealth);
    }
}
