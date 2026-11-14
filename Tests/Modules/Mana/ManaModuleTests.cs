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
        manager.Merge(0, new ManaComponent(currentMana: 100, baseMaximumMana: 100));

        manager.Merge(0, new ManaComponent(currentMana: 50, baseMaximumMana: 50));

        Assert.AreEqual((short)75, manager.GetPackedPool<ManaComponent>().GetReadonly(0).BaseMaximumMana);
    }

    [TestMethod]
    public void Merge_CurrentManaAboveTheBase_IsNotClampedToIt()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new ManaComponent(currentMana: 30, baseMaximumMana: 10));

        manager.Merge(0, new ManaComponent(currentMana: 10, baseMaximumMana: 10));

        Assert.AreEqual(20f, manager.GetPackedPool<ManaComponent>().GetReadonly(0).CurrentMana, 0.001f, "Held within the modified maximum, which a merge can't read, so never cut to the base.");
    }

    /// <summary>Mirrors HealthModuleTests' own regression test: a negative incoming MaximumMana must floor the averaged result at 0 rather than leaving CurrentMana's clamp with min > max.</summary>
    [TestMethod]
    public void Merge_NegativeIncomingMaximumMana_FloorsAveragedMaximumAtZero_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new ManaComponent(currentMana: 0, baseMaximumMana: -100));

        manager.Merge(0, new ManaComponent(currentMana: 0, baseMaximumMana: 0));

        Assert.AreEqual((short)0, manager.GetPackedPool<ManaComponent>().GetReadonly(0).BaseMaximumMana);
    }
}
