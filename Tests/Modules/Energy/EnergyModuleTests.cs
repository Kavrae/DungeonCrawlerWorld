using Engine.ECS.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;

namespace Tests.Modules.Energy;

[TestClass]
public sealed class EnergyModuleTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new EnergyModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void Merge_AveragedMaximumEnergy_StaysWithinShortRange()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new EnergyComponent(currentEnergy: 100, energyRecharge: 10, maximumEnergy: 100));

        manager.Merge(0, new EnergyComponent(currentEnergy: 50, energyRecharge: 5, maximumEnergy: 50));

        Assert.AreEqual((short)75, manager.GetPackedPool<EnergyComponent>().GetReadonly(0).MaximumEnergy);
    }

    /// <summary>
    /// Regression test: merging in a component with a negative MaximumEnergy used to leave
    /// the averaged MaximumEnergy negative too, so the CurrentEnergy clamp right after it
    /// called ClampShort(current, 0, negativeMax) -- min > max, which throws now that
    /// ClampShort's inverted-bounds guard exists. MaximumEnergy must floor at 0 instead.
    /// </summary>
    [TestMethod]
    public void Merge_NegativeIncomingMaximumEnergy_FloorsAveragedMaximumAtZero_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new EnergyComponent(currentEnergy: 0, energyRecharge: 0, maximumEnergy: -100));

        manager.Merge(0, new EnergyComponent(currentEnergy: 0, energyRecharge: 0, maximumEnergy: 0));

        Assert.AreEqual((short)0, manager.GetPackedPool<EnergyComponent>().GetReadonly(0).MaximumEnergy);
    }
}