using Engine.ECS.Components.Stores;
using Game.Modules.Energy.Components;
using Game.Modules.Energy.Systems;

namespace Tests.Modules.Energy;

[TestClass]
public sealed class EnergyRechargeSystemTests
{
    private static PackedComponentPool<EnergyComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref EnergyComponent existing, EnergyComponent incoming) => existing = incoming);

    [TestMethod]
    public void Update_RechargesEnergyByEnergyRechargeAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new EnergyComponent(currentEnergy: 10, energyRecharge: 5, maximumEnergy: 100));
        var system = new EnergyRechargeSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(15, pool.GetReadonly(0).CurrentEnergy);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumEnergy()
    {
        var pool = CreatePool();
        pool.Add(0, new EnergyComponent(currentEnergy: 98, energyRecharge: 10, maximumEnergy: 100));
        var system = new EnergyRechargeSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(100, pool.GetReadonly(0).CurrentEnergy);
    }

    [TestMethod]
    public void Update_ZeroRecharge_LeavesCurrentEnergyUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new EnergyComponent(currentEnergy: 50, energyRecharge: 0, maximumEnergy: 100));
        var system = new EnergyRechargeSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentEnergy);
    }

    /// <summary>
    /// Regression test: PackedComponentPool.TryUpdate bumps its component's version
    /// unconditionally once its delegate runs, so a zero-recharge entity must never reach
    /// TryUpdate at all, or its version would climb every stripe cycle despite never changing.
    /// </summary>
    [TestMethod]
    public void Update_ZeroRecharge_DoesNotBumpVersion()
    {
        var pool = CreatePool();
        pool.Add(0, new EnergyComponent(currentEnergy: 50, energyRecharge: 0, maximumEnergy: 100));
        var system = new EnergyRechargeSystem(pool);
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }

    /// <summary>
    /// Regression test: CurrentEnergy += EnergyRecharge used to compute in short and could
    /// silently overflow/underflow before the subsequent clamp ran. A large negative recharge
    /// against a very negative CurrentEnergy underflows short's range and wraps to a large
    /// positive number -- if that wrapped value were what got clamped, it would land near
    /// MaximumEnergy instead of the mathematically correct 0.
    /// </summary>
    [TestMethod]
    public void Update_LargeNegativeRecharge_ClampsToZeroInsteadOfUnderflowWrapping()
    {
        var pool = CreatePool();
        pool.Add(0, new EnergyComponent(currentEnergy: -32000, energyRecharge: -1000, maximumEnergy: 100));
        var system = new EnergyRechargeSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentEnergy);
    }
}
