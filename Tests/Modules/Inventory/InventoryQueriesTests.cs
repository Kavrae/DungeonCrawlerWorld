using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryQueriesTests
{
    [TestMethod]
    public void CopyStacksForEntity_ReturnsExactlyTheStacksAdded()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var potionId = Guid.NewGuid();
        var hammerId = Guid.NewGuid();
        pool.Add(0, new InventoryItemStackComponent(potionId, quantity: 5));
        pool.Add(0, new InventoryItemStackComponent(hammerId, quantity: 1, isDisabled: true));
        pool.Add(1, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1)); // a different entity's stack, must not leak into entity 0's results.

        var destination = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(pool, 0, destination);

        Assert.HasCount(2, destination);
        Assert.IsTrue(destination.Any(stack => stack.ItemDefinitionId == potionId && stack.Quantity == 5 && !stack.IsDisabled));
        Assert.IsTrue(destination.Any(stack => stack.ItemDefinitionId == hammerId && stack.Quantity == 1 && stack.IsDisabled));
    }

    [TestMethod]
    public void CopyStacksForEntity_CalledAgainWithFewerStacks_ClearsStaleEntriesFromDestination()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));

        var destination = new List<InventoryItemStackComponent> { new(Guid.NewGuid(), quantity: 99) };
        InventoryQueries.CopyStacksForEntity(pool, 0, destination);

        Assert.HasCount(1, destination);
    }

    [TestMethod]
    public void IsInventoryDisabled_NoComponentPresent_DefaultsToFalse()
    {
        var pool = new DirectComponentPool<InventoryDisabledComponent>(initialCapacity: 10, static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);

        Assert.IsFalse(InventoryQueries.IsInventoryDisabled(pool, 0));
    }

    [TestMethod]
    public void IsInventoryDisabled_ComponentPresentAndTrue_ReturnsTrue()
    {
        var pool = new DirectComponentPool<InventoryDisabledComponent>(initialCapacity: 10, static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);
        pool.Add(0, new InventoryDisabledComponent(isDisabled: true));

        Assert.IsTrue(InventoryQueries.IsInventoryDisabled(pool, 0));
    }
}
