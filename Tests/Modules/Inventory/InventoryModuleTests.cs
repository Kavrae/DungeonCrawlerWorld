using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryModuleTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void RegisterComponents_InventoryItemStackComponent_MultiPoolAcceptsMultipleStacksPerEntity()
    {
        var manager = CreateRegisteredManager();
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();

        pool.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));
        pool.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 2));

        Assert.AreEqual(2, pool.CountForEntity(0));
    }

    [TestMethod]
    public void RegisterComponents_InventoryDisabledComponent_MergeOverwritesWithIncomingValue()
    {
        var manager = CreateRegisteredManager();

        manager.Merge(0, new InventoryDisabledComponent(isDisabled: true));
        manager.Merge(0, new InventoryDisabledComponent(isDisabled: false));

        Assert.IsFalse(manager.GetPackedPool<InventoryDisabledComponent>().GetReadonly(0).IsDisabled);
    }
}
