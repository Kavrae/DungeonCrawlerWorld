using Engine.ECS.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryActionsTests
{
    /// <summary>A fixed player id well outside the small entity ids these tests use for source/destination, so "is this entity the player" reads as false for every entity under test unless a test explicitly aliases one to it.</summary>
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static readonly FakePlayerQuery NoEntityIsThePlayer = new(playerEntityId: -1);

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

        var result = InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 0, stackInstanceId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        Assert.AreEqual(1, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(0));
    }

    [TestMethod]
    public void TryTransferStack_UnknownStackInstanceId_ReturnsFalse()
    {
        var manager = CreateRegisteredManager();

        var result = InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 1, Guid.NewGuid(), NoEntityIsThePlayer);

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

        var result = InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 1, sourceStack.StackInstanceId, NoEntityIsThePlayer);

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

        var result = InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, pool.CountForEntity(0));
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount, pool.CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsThePlayerAtWhatWouldOtherwiseBeTheCap_StillSucceeds()
    {
        var manager = CreateRegisteredManager();
        var playerQuery = new FakePlayerQuery(playerEntityId: 1);
        for (var i = 0; i < InventoryCapacity.MaxNonPlayerStackCount; i++)
        {
            InventoryActions.AddItem(manager, entityId: 1, Guid.NewGuid(), quantity: 1);
        }

        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);

        var result = InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, playerQuery);

        Assert.IsTrue(result);
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount + 1, manager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(1));
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_SameSourceAndDestination_ReturnsFalseAndDoesNotModify()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, sourceEntityId: 0, destinationEntityId: 0, itemId, NoEntityIsThePlayer);

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

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, sourceEntityId: 0, destinationEntityId: 1, itemId, NoEntityIsThePlayer);

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

        var result = InventoryActions.TryTransferAllStacksOfItem(manager, sourceEntityId: 0, destinationEntityId: 1, itemId, NoEntityIsThePlayer);

        Assert.IsFalse(result);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, pool.CountForEntity(0));
        Assert.AreEqual(InventoryCapacity.MaxNonPlayerStackCount - 1, pool.CountForEntity(1));
    }

    [TestMethod]
    public void AddItem_NewStack_StampsFirstAcquiredWithinCallWindow()
    {
        var manager = CreateRegisteredManager();
        var beforeTicks = DateTime.UtcNow.Ticks;

        InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);

        var afterTicks = DateTime.UtcNow.Ticks;
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var stamped = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).FirstAcquiredUtcTicks;
        Assert.IsTrue(stamped >= beforeTicks && stamped <= afterTicks);
    }

    [TestMethod]
    public void AddItem_MergeIntoExistingStack_PreservesOriginalFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 5);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).FirstAcquiredUtcTicks;
        Thread.Sleep(5);

        InventoryActions.AddItem(manager, entityId: 0, itemId, quantity: 3);

        Assert.AreEqual(originalFirstAcquired, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).FirstAcquiredUtcTicks);
    }

    [TestMethod]
    public void AddDivergentItem_DifferentOverrides_SecondStackGetsALaterFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();

        var firstId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        Thread.Sleep(5);
        var secondId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));

        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, firstId, out var firstStack));
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, secondId, out var secondStack));
        Assert.IsGreaterThan(firstStack.FirstAcquiredUtcTicks, secondStack.FirstAcquiredUtcTicks);
    }

    [TestMethod]
    public void AddDivergentItem_MergesIntoExistingDivergentStack_PreservesOriginalFirstAcquired()
    {
        var manager = CreateRegisteredManager();
        var itemId = Guid.NewGuid();
        var stackInstanceId = InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, stackInstanceId, out var originalStack));
        Thread.Sleep(5);

        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, stackInstanceId, out var mergedStack));
        Assert.AreEqual(originalStack.FirstAcquiredUtcTicks, mergedStack.FirstAcquiredUtcTicks);
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsNotThePlayer_PreservesFirstAcquiredAcrossTheMove()
    {
        var manager = CreateRegisteredManager();
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).FirstAcquiredUtcTicks;

        InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: 1, stackInstanceId, NoEntityIsThePlayer);

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 1, stackInstanceId, out var movedStack));
        Assert.AreEqual(originalFirstAcquired, movedStack.FirstAcquiredUtcTicks);
    }

    [TestMethod]
    public void TryTransferStack_DestinationIsThePlayer_ResetsFirstAcquiredToNow()
    {
        // Simulates "Take" from a corpse/loot window into the player's own inventory -- the item
        // may have sat in the source's inventory for a long time, but landing in the player's own
        // inventory should read as freshly acquired.
        var manager = CreateRegisteredManager();
        var playerQuery = new FakePlayerQuery(playerEntityId: 1);
        var stackInstanceId = InventoryActions.AddItem(manager, entityId: 0, Guid.NewGuid(), quantity: 1);
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalFirstAcquired = pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).FirstAcquiredUtcTicks;
        Thread.Sleep(5);

        var beforeTransferTicks = DateTime.UtcNow.Ticks;
        InventoryActions.TryTransferStack(manager, sourceEntityId: 0, destinationEntityId: playerQuery.PlayerEntityId, stackInstanceId, playerQuery);
        var afterTransferTicks = DateTime.UtcNow.Ticks;

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, playerQuery.PlayerEntityId, stackInstanceId, out var movedStack));
        Assert.IsGreaterThan(originalFirstAcquired, movedStack.FirstAcquiredUtcTicks);
        Assert.IsTrue(movedStack.FirstAcquiredUtcTicks >= beforeTransferTicks && movedStack.FirstAcquiredUtcTicks <= afterTransferTicks);
    }

    [TestMethod]
    public void TryTransferAllStacksOfItem_DestinationIsThePlayer_ResetsFirstAcquiredOnEveryMovedStack()
    {
        var manager = CreateRegisteredManager();
        var playerQuery = new FakePlayerQuery(playerEntityId: 1);
        var itemId = Guid.NewGuid();
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 5));
        InventoryActions.AddDivergentItem(manager, entityId: 0, CreateDefinition(itemId, charges: 4));
        var pool = manager.GetMultiPool<InventoryItemStackComponent>();
        var originalStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, originalStacks);
        Thread.Sleep(5);

        var beforeTransferTicks = DateTime.UtcNow.Ticks;
        InventoryActions.TryTransferAllStacksOfItem(manager, sourceEntityId: 0, destinationEntityId: playerQuery.PlayerEntityId, itemId, playerQuery);
        var afterTransferTicks = DateTime.UtcNow.Ticks;

        var movedStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, playerQuery.PlayerEntityId, movedStacks);
        Assert.HasCount(2, movedStacks);
        foreach (var movedStack in movedStacks)
        {
            var original = originalStacks.Single(stack => stack.StackInstanceId == movedStack.StackInstanceId);
            Assert.IsGreaterThan(original.FirstAcquiredUtcTicks, movedStack.FirstAcquiredUtcTicks);
            Assert.IsTrue(movedStack.FirstAcquiredUtcTicks >= beforeTransferTicks && movedStack.FirstAcquiredUtcTicks <= afterTransferTicks);
        }
    }
}
