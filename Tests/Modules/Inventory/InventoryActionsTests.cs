using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryActionsTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4);
        new InventoryModule().RegisterComponents(manager);
        return manager;
    }

    [TestMethod]
    public void AddItem_SameItemDefinitionTwice_StacksIntoOneEntryWithSummedQuantity()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 5);
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(8, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).Quantity);
    }

    [TestMethod]
    public void AddItem_DifferentItemDefinitions_CreatesTwoDistinctStacks()
    {
        var manager = CreateRegisteredManager();

        InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);
        InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);

        Assert.AreEqual(2, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void SetStackDisabled_MatchingStack_FlipsOnlyThatStacksIsDisabled()
    {
        var manager = CreateRegisteredManager();
        var disabledItemId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, disabledItemId, quantity: 1);
        InventoryActions.AddItem(manager, entityId: 0, otherItemId, quantity: 1);

        InventoryActions.SetStackDisabled(manager, entityId: 0, disabledItemId, disabled: true);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(manager.GetMultiPool<InventoryItemStackComponent>(), 0, stacks);

        Assert.IsTrue(stacks.Single(stack => stack.ItemDefinitionId == disabledItemId).IsDisabled);
        Assert.IsFalse(stacks.Single(stack => stack.ItemDefinitionId == otherItemId).IsDisabled);
    }

    [TestMethod]
    public void ConsumeItem_StackAboveOne_DecrementsQuantityWithoutRemovingStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);

        InventoryActions.ConsumeItem(manager, entityId: 0, itemId);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(2, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).Quantity);
    }

    [TestMethod]
    public void ConsumeItem_LastOneInStack_RemovesTheStackEntirely()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 1);

        InventoryActions.ConsumeItem(manager, entityId: 0, itemId);

        Assert.AreEqual(0, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void ConsumeItem_ItemNotInInventory_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();

        InventoryActions.ConsumeItem(manager, entityId: 0, Guid.NewGuid());
    }

    [TestMethod]
    public void SetInventoryDisabled_ThenQueried_RoundTrips()
    {
        var manager = CreateRegisteredManager();

        InventoryActions.SetInventoryDisabled(manager, entityId: 0, disabled: true);
        Assert.IsTrue(InventoryQueries.IsInventoryDisabled(manager.GetDirectPool<InventoryDisabledComponent>(), 0));

        InventoryActions.SetInventoryDisabled(manager, entityId: 0, disabled: false);
        Assert.IsFalse(InventoryQueries.IsInventoryDisabled(manager.GetDirectPool<InventoryDisabledComponent>(), 0));
    }
}
