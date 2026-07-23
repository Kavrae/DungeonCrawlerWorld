using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthRegenSystemTests
{
    private static PackedComponentPool<HealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_RegeneratesHealthByHealthRegenAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 10, maximumHealth: 200));
        var system = new HealthRegenSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 195, healthRegen: 10, maximumHealth: 200));
        var system = new HealthRegenSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(200, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ZeroRegen_LeavesCurrentHealthUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 200));
        var system = new HealthRegenSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    /// <summary>
    /// Regression test: PackedComponentPool.TryUpdate bumps its component's version
    /// unconditionally once its delegate runs, so a zero-regen entity must never reach
    /// TryUpdate at all, or its version would climb every stripe cycle despite never changing.
    /// </summary>
    [TestMethod]
    public void Update_ZeroRegen_DoesNotBumpVersion()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 200));
        var system = new HealthRegenSystem(pool);
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }

    /// <summary>
    /// Regression test: CurrentHealth += HealthRegen used to compute in short and could
    /// silently overflow/underflow before the subsequent clamp ran. A large negative regen
    /// against a very negative CurrentHealth underflows short's range and wraps to a large
    /// positive number -- if that wrapped value were what got clamped, it would land near
    /// MaximumHealth instead of the mathematically correct 0.
    /// </summary>
    [TestMethod]
    public void Update_LargeNegativeRegen_ClampsToZeroInsteadOfUnderflowWrapping()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: -32000, healthRegen: -1000, maximumHealth: 200));
        var system = new HealthRegenSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }
}