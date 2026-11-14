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
        manager.Merge(0, new SimpleHealthComponent(currentHealth: 100, baseMaximumHealth: 100));

        manager.Merge(0, new SimpleHealthComponent(currentHealth: 50, baseMaximumHealth: 50));

        Assert.AreEqual((short)75, manager.GetPackedPool<SimpleHealthComponent>().GetReadonly(0).BaseMaximumHealth);
    }

    [TestMethod]
    public void Merge_CurrentHealthAboveTheBase_MovesWithTheBaseAndIsNotClampedToIt()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new SimpleHealthComponent(currentHealth: 200, baseMaximumHealth: 100));

        manager.Merge(0, new SimpleHealthComponent(currentHealth: 200, baseMaximumHealth: 200));

        var health = manager.GetPackedPool<SimpleHealthComponent>().GetReadonly(0);
        Assert.AreEqual(150f, health.BaseMaximumHealth, 0.001f);
        Assert.AreEqual(250f, health.CurrentHealth, 0.001f, "Current health already includes the entity's modifiers, so it moves by the base's 50 and is never cut to the base.");
    }

    /// <summary>
    /// Regression test: merging in a component with a negative MaximumHealth used to leave
    /// the averaged MaximumHealth negative too, so the CurrentHealth clamp right after it
    /// called ClampUShort(current, 0, negativeMax) -- min > max, which throws now that
    /// ClampUShort's inverted-bounds guard exists. MaximumHealth must floor at 0 instead.
    /// </summary>
    [TestMethod]
    public void Merge_NegativeIncomingMaximumHealth_FloorsAveragedMaximumAtZero_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new SimpleHealthComponent(currentHealth: 0, baseMaximumHealth: -100));

        manager.Merge(0, new SimpleHealthComponent(currentHealth: 0, baseMaximumHealth: 0));

        Assert.AreEqual((short)0, manager.GetPackedPool<SimpleHealthComponent>().GetReadonly(0).BaseMaximumHealth);
    }
}
