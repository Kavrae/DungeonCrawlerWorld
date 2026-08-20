using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;

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

    private static ItemDefinition CreateDefinition(Guid id, ushort charges, int? maxStackSize = null) =>
        new(id, $"Test Wand ({charges})", SpriteName: null, Glyph: "?", Color.White, Tags: [], Effects: [], MaxStackSize: maxStackSize);

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
    public void AddItemWithOverride_TwoEquivalentOverrides_MergesIntoOneStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 4);
        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 6);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(10, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).Quantity);
        Assert.IsFalse(pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).IsDivergent);
    }

    [TestMethod]
    public void AddItemWithOverride_QuantityExceedsMaxStackSize_SpillsIntoASecondStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10, maxStackSize: 10), quantity: 15);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, stacks);

        Assert.HasCount(2, stacks);
        Assert.AreEqual(15, stacks.Sum(stack => stack.Quantity));
        Assert.IsTrue(stacks.Any(stack => stack.Quantity == 10));
        Assert.IsTrue(stacks.Any(stack => stack.Quantity == 5));
    }

    [TestMethod]
    public void AddDivergentItem_FirstCall_CreatesNewQuantityOneDivergentStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        var stackInstanceId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, stackInstanceId, out var stack));
        Assert.AreEqual(1, stack.Quantity);
        Assert.IsTrue(stack.IsDivergent);
    }

    [TestMethod]
    public void AddDivergentItem_TwoStructurallyEqualOverrides_MergeIntoOneStack()
    {
        // Mirrors "two swords independently enchanted to the exact same +1 damage bonus" --
        // separately-constructed but structurally-identical Overrides must still share one stack.
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        var firstId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        var secondId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(firstId, secondId);
        Assert.AreEqual(2, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).Quantity);
    }

    [TestMethod]
    public void AddDivergentItem_DifferentOverrides_CreateSeparateStacks()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));

        Assert.AreEqual(2, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void PeelOneIntoDivergentStack_MultiUnitPlainStack_DecrementsOriginalAndCreatesDivergentStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 3);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var plainStackInstanceId = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).StackInstanceId;

        InventoryActions.PeelOneIntoDivergentStack(manager, entityId: 0, plainStackInstanceId, CreateDefinition(itemId, charges: 9));

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, stacks);
        Assert.HasCount(2, stacks);
        Assert.IsTrue(stacks.Any(stack => !stack.IsDivergent && stack.Quantity == 2));
        Assert.IsTrue(stacks.Any(stack => stack.IsDivergent && stack.Quantity == 1));
    }

    [TestMethod]
    public void PeelOneIntoDivergentStack_ThenGrantAnotherStandardWand_LeavesNoOrphanedStack()
    {
        // Give the player a single standard wand, fire it (peeling it into a divergent stack),
        // then give them another standard wand -- exactly two stacks should exist at the end (one
        // divergent, one plain), and the original single-unit plain stack must not linger as an
        // orphaned Quantity: 0 entry once it's fully consumed.
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 1);
        var originalStackInstanceId = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).StackInstanceId;

        InventoryActions.PeelOneIntoDivergentStack(manager, entityId: 0, originalStackInstanceId, CreateDefinition(itemId, charges: 9));

        // The original plain stack must be gone entirely, not left behind at Quantity: 0.
        Assert.IsFalse(InventoryQueries.TryFindByStackInstanceId(pool, 0, originalStackInstanceId, out _));
        Assert.AreEqual(1, pool.CountForEntity(0));

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 1);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, stacks);
        Assert.HasCount(2, stacks);
        Assert.IsTrue(stacks.Any(stack => stack.IsDivergent && stack.Quantity == 1));
        Assert.IsTrue(stacks.Any(stack => !stack.IsDivergent && stack.Quantity == 1));
    }

    [TestMethod]
    public void ConsumeItemByStackInstanceId_LastUnit_RemovesTheStackEntirely()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 1);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var stackInstanceId = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).StackInstanceId;

        InventoryActions.ConsumeItemByStackInstanceId(manager, entityId: 0, stackInstanceId);

        Assert.AreEqual(0, pool.CountForEntity(0));
    }

    [TestMethod]
    public void ConsumeItemByStackInstanceId_UnknownStackInstanceId_DoesNotThrow()
    {
        var manager = CreateRegisteredManager();

        InventoryActions.ConsumeItemByStackInstanceId(manager, entityId: 0, Guid.NewGuid());
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
