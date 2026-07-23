using Engine.ECS.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthModuleTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new HealthModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void Merge_AveragedMaximumHealth_StaysWithinShortRange()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new HealthComponent(currentHealth: 100, healthRegen: 10, maximumHealth: 100));

        manager.Merge(0, new HealthComponent(currentHealth: 50, healthRegen: 5, maximumHealth: 50));

        Assert.AreEqual((short)75, manager.GetPackedPool<HealthComponent>().GetReadonly(0).MaximumHealth);
    }

    /// <summary>
    /// Regression test: merging in a component with a negative MaximumHealth used to leave
    /// the averaged MaximumHealth negative too, so the CurrentHealth clamp right after it
    /// called ClampShort(current, 0, negativeMax) -- min > max, which throws now that
    /// ClampShort's inverted-bounds guard exists. MaximumHealth must floor at 0 instead.
    /// </summary>
    [TestMethod]
    public void Merge_NegativeIncomingMaximumHealth_FloorsAveragedMaximumAtZero_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new HealthComponent(currentHealth: 0, healthRegen: 0, maximumHealth: -100));

        manager.Merge(0, new HealthComponent(currentHealth: 0, healthRegen: 0, maximumHealth: 0));

        Assert.AreEqual((short)0, manager.GetPackedPool<HealthComponent>().GetReadonly(0).MaximumHealth);
    }
}