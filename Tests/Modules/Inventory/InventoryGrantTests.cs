using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryGrantTests
{
    private static readonly Guid TestItemId = new("11111111-1111-1111-1111-111111111111");

    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new InventoryModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void EnsureInventoryComponentExists_EntityHasNone_GrantsIt()
    {
        var manager = CreateRegisteredManager();

        InventoryGrant.EnsureInventoryComponentExists(manager, 0);

        Assert.IsTrue(manager.GetPackedPool<InventoryComponent>().Has(0));
    }

    [TestMethod]
    public void EnsureInventoryComponentExists_EntityAlreadyHasIt_StaysAsIsAndDoesNotThrow()
    {
        var manager = CreateRegisteredManager();
        InventoryGrant.EnsureInventoryComponentExists(manager, 0);

        InventoryGrant.EnsureInventoryComponentExists(manager, 0);

        Assert.IsTrue(manager.GetPackedPool<InventoryComponent>().Has(0));
    }

    [TestMethod]
    public void AddItem_FirstEverItemGrant_GrantsInventoryComponent()
    {
        var manager = CreateRegisteredManager();
        Assert.IsFalse(manager.GetPackedPool<InventoryComponent>().Has(0));

        InventoryActions.AddItem(manager, 0, TestItemId, quantity: 1);

        Assert.IsTrue(manager.GetPackedPool<InventoryComponent>().Has(0));
    }

    /// <summary>The permanence guarantee InventoryComponent's own doc comment describes -- "no items = no inventory" only applies before the first grant; once granted, running out of items never takes it away.</summary>
    [TestMethod]
    public void AddItem_ThenConsumeItemDownToZero_InventoryComponentIsNotRemoved()
    {
        var manager = CreateRegisteredManager();
        InventoryActions.AddItem(manager, 0, TestItemId, quantity: 1);

        InventoryActions.ConsumeItem(manager, 0, TestItemId);

        Assert.IsFalse(InventoryQueries.TryGetStack(manager.GetMultiPool<InventoryItemStackComponent>(), 0, TestItemId, out _), "Sanity check: the stack itself really is gone.");
        Assert.IsTrue(manager.GetPackedPool<InventoryComponent>().Has(0), "InventoryComponent must survive even once every stack is consumed.");
    }
}
