using Engine.ECS.Components;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaModuleTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new ManaModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void Merge_AveragedMaximumMana_StaysWithinShortRange()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new ManaComponent(currentMana: 100, maximumMana: 100));

        manager.Merge(0, new ManaComponent(currentMana: 50, maximumMana: 50));

        Assert.AreEqual((short)75, manager.GetPackedPool<ManaComponent>().GetReadonly(0).MaximumMana);
    }

    /// <summary>Mirrors HealthModuleTests' own regression test: a negative incoming MaximumMana must floor the averaged result at 0 rather than leaving CurrentMana's clamp with min > max.</summary>
    [TestMethod]
    public void Merge_NegativeIncomingMaximumMana_FloorsAveragedMaximumAtZero_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new ManaComponent(currentMana: 0, maximumMana: -100));

        manager.Merge(0, new ManaComponent(currentMana: 0, maximumMana: 0));

        Assert.AreEqual((short)0, manager.GetPackedPool<ManaComponent>().GetReadonly(0).MaximumMana);
    }
}
