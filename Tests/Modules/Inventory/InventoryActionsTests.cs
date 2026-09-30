using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
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

    private static ItemDefinition CreateDefinition(Guid id, ushort charges) =>
        new(id, $"Test Wand ({charges})", SpriteName: null, Glyph: "?", Color.White, Tags: [], Effects: []);

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
    public void AddItem_NoOverrideGiven_CapsAtDefaultMaxStackSizeOf999()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 1500);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));

        var quantities = new List<ushort>();
        for (var denseIndex = pool.GetFirstDenseIndex(0); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            quantities.Add(pool.GetReadonlyByDenseIndex(denseIndex).Quantity);
        }
        quantities.Sort();

        CollectionAssert.AreEqual(new ushort[] { 501, 999 }, quantities);
    }

    [TestMethod]
    public void GetEffectiveMaxStackSize_EntityWithNoOverride_ReturnsDefault()
    {
        var manager = CreateRegisteredManager();

        Assert.AreEqual(InventoryActions.DefaultMaxStackSize, InventoryActions.GetEffectiveMaxStackSize(manager, entityId: 0));
    }

    [TestMethod]
    public void GetEffectiveMaxStackSize_EntityWithOverride_ReturnsOverride()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(entityId: 0, new MaxStackSizeComponent(500));

        Assert.AreEqual((ushort)500, InventoryActions.GetEffectiveMaxStackSize(manager, entityId: 0));
    }

    [TestMethod]
    public void AddItem_QuantityExceedsEntitysMaxStackSize_SpillsOverflowIntoANewStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        manager.Merge(entityId: 0, new MaxStackSizeComponent(5));

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 7);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));

        var quantities = new List<ushort>();
        for (var denseIndex = pool.GetFirstDenseIndex(0); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            quantities.Add(pool.GetReadonlyByDenseIndex(denseIndex).Quantity);
        }
        quantities.Sort();

        CollectionAssert.AreEqual(new ushort[] { 2, 5 }, quantities);
    }

    [TestMethod]
    public void AddItem_MergingWouldExceedEntitysMaxStackSize_ToppedUpStackPlusNewOverflowStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        manager.Merge(entityId: 0, new MaxStackSizeComponent(5));

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 4);
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 4);

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));

        var quantities = new List<ushort>();
        for (var denseIndex = pool.GetFirstDenseIndex(0); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            quantities.Add(pool.GetReadonlyByDenseIndex(denseIndex).Quantity);
        }
        quantities.Sort();

        CollectionAssert.AreEqual(new ushort[] { 3, 5 }, quantities);
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
    public void AddItem_OntoAnOverriddenStackOfTheSameItem_MakesItsOwnPlainStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 4);
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 2);

        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(manager.GetMultiPool<InventoryItemStackComponent>(), 0, stacks);
        Assert.HasCount(2, stacks);
        Assert.AreEqual(4, stacks.Single(stack => stack.Override is not null).Quantity);
        Assert.AreEqual(2, stacks.Single(stack => stack.Override is null).Quantity);
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
    public void AddItemWithOverride_QuantityExceedsEntitysMaxStackSize_SpillsIntoASecondStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        manager.Merge(entityId: 0, new MaxStackSizeComponent(10));

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 10), quantity: 15);

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

        InventoryActions.ConsumeItemByStackInstanceId(manager, entityId: 0, stackInstanceId: 9999);
    }

    [TestMethod]
    public void SetInventoryDisabled_ThenQueried_RoundTrips()
    {
        var manager = CreateRegisteredManager();

        InventoryActions.SetInventoryDisabled(manager, entityId: 0, disabled: true);
        Assert.IsTrue(InventoryQueries.IsInventoryDisabled(manager.GetPackedPool<InventoryDisabledComponent>(), 0));

        InventoryActions.SetInventoryDisabled(manager, entityId: 0, disabled: false);
        Assert.IsFalse(InventoryQueries.IsInventoryDisabled(manager.GetPackedPool<InventoryDisabledComponent>(), 0));
    }

    [TestMethod]
    public void TryTransferStack_SameSourceAndDestination_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 1);

        var result = InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 0, stackInstanceId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(1, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void TryTransferStack_UnknownStackInstanceId_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();

        var result = InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, stackInstanceId: 9999, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryTransferStack_MovesStackPreservingIdentityAndDoesNotMergeWithExistingDestinationStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        // Destination already owns a stack of the same item -- the transferred stack must land as
        // its own distinct entry, not merge into this one (stack splitting/merging is a separate,
        // not-yet-built feature).
        InventoryActions.AddItem(manager, entityId: 1, itemId, quantity: 2);

        InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 7), quantity: 3);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var sourceStack = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0));
        InventoryActions.SetStackDisabled(manager, entityId: 0, itemId, disabled: true);

        var result = InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, sourceStack.StackInstanceId, TestPlayerQuery.NoPlayer);

        Assert.IsTrue(result);
        Assert.AreEqual(0, pool.CountForEntity(0));
        Assert.AreEqual(2, pool.CountForEntity(1));

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 1, sourceStack.StackInstanceId, out var movedStack));
        Assert.AreEqual(sourceStack.Quantity, movedStack.Quantity);
        Assert.IsTrue(movedStack.IsDisabled);
        Assert.AreEqual(sourceStack.Override, movedStack.Override);
    }

    [TestMethod]
    public void TryTransferStack_DestinationNonPlayerAtCap_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            InventoryActions.AddItem(manager, entityId: 1, Guid.NewGuid(), quantity: 1);
        }

        var sourceItemId = Guid.NewGuid();
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, sourceItemId, quantity: 1);

        var result = InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount, pool.CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsThePlayerAtWhatWouldOtherwiseBeTheCap_StillSucceeds()
    {
        var manager = CreateRegisteredManager();
        var playerQuery = new TestPlayerQuery(playerEntityId: 1);
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            InventoryActions.AddItem(manager, entityId: 1, Guid.NewGuid(), quantity: 1);
        }

        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);

        var result = InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, playerQuery);

        Assert.IsTrue(result);
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount + 1, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_SameSourceAndDestination_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 0, itemId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(1, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_MergedDivergentStacks_MovesEveryUnderlyingStack()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 3));

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, itemId, TestPlayerQuery.NoPlayer);

        Assert.IsTrue(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(0, pool.CountForEntity(0));
        Assert.AreEqual(3, pool.CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_DestinationLacksRoomForWholeBatch_RefusesEntireBatch()
    {
        var manager = CreateRegisteredManager();
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount - 1; i++)
        {
            InventoryActions.AddItem(manager, entityId: 1, Guid.NewGuid(), quantity: 1);
        }

        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, itemId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount - 1, pool.CountForEntity(1));
    }

    [TestMethod]
    public void AddItem_NewStack_StampsAFreshAcquiredSequence()
    {
        var manager = CreateRegisteredManager();
        var before = InventoryItemStackComponent.NextAcquiredSequence();

        InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);

        var after = InventoryItemStackComponent.NextAcquiredSequence();
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var stamped = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).AcquiredSequence;
        Assert.IsGreaterThan(before, stamped);
        Assert.IsLessThan(after, stamped);
    }

    [TestMethod]
    public void AddItem_MergeIntoExistingStack_PreservesOriginalFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 5);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).AcquiredSequence;

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);

        Assert.AreEqual(originalFirstAcquired, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).AcquiredSequence);
    }

    [TestMethod]
    public void AddDivergentItem_DifferentOverrides_SecondStackGetsALaterFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        var firstId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        var secondId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, firstId, out var firstStack));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, secondId, out var secondStack));
        Assert.IsGreaterThan(firstStack.AcquiredSequence, secondStack.AcquiredSequence);
    }

    [TestMethod]
    public void AddDivergentItem_MergesIntoExistingDivergentStack_PreservesOriginalFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var stackInstanceId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, stackInstanceId, out var originalStack));

        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, stackInstanceId, out var mergedStack));
        Assert.AreEqual(originalStack.AcquiredSequence, mergedStack.AcquiredSequence);
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsNotThePlayer_PreservesFirstAcquiredAcrossTheMove()
    {
        var manager = CreateRegisteredManager();
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).AcquiredSequence;

        InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, TestPlayerQuery.NoPlayer);

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 1, stackInstanceId, out var movedStack));
        Assert.AreEqual(originalFirstAcquired, movedStack.AcquiredSequence);
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsThePlayer_ResetsFirstAcquiredToNow()
    {
        // Simulates "Take" from a corpse/loot window into the player's own inventory -- the item
        // may have sat in the source's inventory for a long time, but landing in the player's own
        // inventory should read as freshly acquired.
        var manager = CreateRegisteredManager();
        var playerQuery = new TestPlayerQuery(playerEntityId: 1);
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).AcquiredSequence;

        var beforeTransfer = InventoryItemStackComponent.NextAcquiredSequence();
        InventoryActions.TryTransferStack(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: playerQuery.PlayerEntityId, stackInstanceId, playerQuery);
        var afterTransfer = InventoryItemStackComponent.NextAcquiredSequence();

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, playerQuery.PlayerEntityId, stackInstanceId, out var movedStack));
        Assert.IsGreaterThan(originalFirstAcquired, movedStack.AcquiredSequence);
        Assert.IsGreaterThan(beforeTransfer, movedStack.AcquiredSequence);
        Assert.IsLessThan(afterTransfer, movedStack.AcquiredSequence);
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_DestinationIsThePlayer_ResetsFirstAcquiredOnEveryMovedStack()
    {
        var manager = CreateRegisteredManager();
        var playerQuery = new TestPlayerQuery(playerEntityId: 1);
        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, originalStacks);

        var beforeTransfer = InventoryItemStackComponent.NextAcquiredSequence();
        InventoryActions.TryTransferAllStacksOfItem(manager, new ItemCatalog(), sourceEntityId: 0, destinationEntityId: playerQuery.PlayerEntityId, itemId, playerQuery);
        var afterTransfer = InventoryItemStackComponent.NextAcquiredSequence();

        var movedStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, playerQuery.PlayerEntityId, movedStacks);
        Assert.HasCount(2, movedStacks);
        foreach (var movedStack in movedStacks)
        {
            var original = originalStacks.Single(stack => stack.StackInstanceId == movedStack.StackInstanceId);
            Assert.IsGreaterThan(original.AcquiredSequence, movedStack.AcquiredSequence);
            Assert.IsGreaterThan(beforeTransfer, movedStack.AcquiredSequence);
            Assert.IsLessThan(afterTransfer, movedStack.AcquiredSequence);
        }
    }

    private static ItemCatalog CatalogWithUntradeable(Guid itemId)
    {
        var catalog = new ItemCatalog();
        catalog.Register(new ItemDefinition(itemId, "Untradeable", SpriteName: null, Glyph: "u", Color.White, Tags: [], Effects: [], CanTrade: false));
        return catalog;
    }

    [TestMethod]
    public void TryTransferStack_UntradeableItem_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);

        var result = InventoryActions.TryTransferStack(manager, CatalogWithUntradeable(itemId), sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(0, pool.CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_UntradeableItem_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);
        InventoryActions.AddItemWithOverride(manager, entityId: 0, new ItemDefinition(itemId, "Untradeable", SpriteName: null, Glyph: "u", Color.White, Tags: [], Effects: [], CanTrade: false), quantity: 1);

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, CatalogWithUntradeable(itemId), sourceEntityId: 0, destinationEntityId: 1, itemId, TestPlayerQuery.NoPlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));
        Assert.AreEqual(0, pool.CountForEntity(1));
    }

    [TestMethod]
    public void ItemHotkeyBindingQueries_CanBind_IsFalseOnlyForALootbox()
    {
        Assert.IsTrue(ItemHotkeyBindingQueries.CanBind(CreateDefinition(Guid.NewGuid(), charges: 1)));
        Assert.IsFalse(ItemHotkeyBindingQueries.CanBind(CreateDefinition(Guid.NewGuid(), charges: 1) with { Tags = [Game.Modules.Tag.Lootbox] }));
    }

    private static uint AddSeparateStack(ComponentManager manager, Guid itemId, ushort quantity)
    {
        var stack = new InventoryItemStackComponent(itemId, quantity);
        manager.GetMultiPool<InventoryItemStackComponent>().Add(0, stack);
        return stack.StackInstanceId;
    }

    [TestMethod]
    public void MergeIntoEquivalentStack_EquivalentStack_MovesEveryUnitIntoTheExistingStackAndRemovesTheSource()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var existingStackId = InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 2);
        var arrivingStackId = AddSeparateStack(manager, itemId, quantity: 3);

        var holdingStackId = InventoryActions.MergeIntoEquivalentStack(manager, entityId: 0, arrivingStackId);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(existingStackId, holdingStackId);
        Assert.AreEqual(1, stacks.CountForEntity(0));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, 0, existingStackId, out var merged));
        Assert.AreEqual(5, merged.Quantity);
    }

    [TestMethod]
    public void MergeIntoEquivalentStack_ExistingStackNearItsCap_LeavesTheRemainderInTheSource()
    {
        var manager = CreateRegisteredManager();
        manager.Merge(0, new MaxStackSizeComponent(4));
        var itemId = Guid.NewGuid();
        var existingStackId = InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);
        var arrivingStackId = AddSeparateStack(manager, itemId, quantity: 3);

        var holdingStackId = InventoryActions.MergeIntoEquivalentStack(manager, entityId: 0, arrivingStackId);

        var stacks = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(arrivingStackId, holdingStackId);
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, 0, existingStackId, out var existing));
        Assert.AreEqual(4, existing.Quantity);
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, 0, arrivingStackId, out var remainder));
        Assert.AreEqual(2, remainder.Quantity);
    }

    [TestMethod]
    public void MergeIntoEquivalentStack_DifferentOverride_LeavesBothStacksAlone()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 2);
        var arrivingStackId = InventoryActions.AddItemWithOverride(manager, entityId: 0, CreateDefinition(itemId, charges: 3), quantity: 1);

        var holdingStackId = InventoryActions.MergeIntoEquivalentStack(manager, entityId: 0, arrivingStackId);

        Assert.AreEqual(arrivingStackId, holdingStackId);
        Assert.AreEqual(2, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void MergeIntoEquivalentStack_DisabledAgainstEnabled_LeavesBothStacksAlone()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 2);
        var disabledStack = new InventoryItemStackComponent(itemId, 1, isDisabled: true);
        manager.GetMultiPool<InventoryItemStackComponent>().Add(0, disabledStack);

        InventoryActions.MergeIntoEquivalentStack(manager, entityId: 0, disabledStack.StackInstanceId);

        Assert.AreEqual(2, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }
}
