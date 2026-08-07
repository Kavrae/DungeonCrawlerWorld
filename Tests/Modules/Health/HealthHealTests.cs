using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthHealTests
{
    private static PackedComponentPool<HealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Apply_RaisesCurrentHealthByAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, maximumHealth: 100));

        HealthHeal.Apply(pool, 0, 10);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 95, maximumHealth: 100));

        HealthHeal.Apply(pool, 0, 50);

        Assert.AreEqual(100, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Apply_NoHealthComponent_DoesNotThrow()
    {
        var pool = CreatePool();

        HealthHeal.Apply(pool, 0, 10);
    }
}
