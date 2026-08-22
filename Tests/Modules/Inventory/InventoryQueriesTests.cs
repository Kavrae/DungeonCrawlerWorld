using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryQueriesTests
{
    private static ItemDefinition CreateDefinition(Guid id, string name = "Test Item") =>
        new(id, name, SpriteName: null, Glyph: "?", Color.White, Tags: [], Effects: []);

    [TestMethod]
    public void TryResolveEffectiveItem_StackHasNoOverride_FallsThroughToCatalog()
    {
        var itemId = Guid.NewGuid();
        var catalog = new ItemCatalog();
        catalog.Register(CreateDefinition(itemId, "Health Potion"));
        var stack = new InventoryItemStackComponent(itemId, quantity: 1);

        Assert.IsTrue(InventoryQueries.TryResolveEffectiveItem(catalog, in stack, out var definition));
        Assert.AreEqual("Health Potion", definition.Name);
    }

    [TestMethod]
    public void TryResolveEffectiveItem_StackHasOverride_ReturnsTheOverrideInsteadOfTheCatalogEntry()
    {
        var itemId = Guid.NewGuid();
        var catalog = new ItemCatalog();
        catalog.Register(CreateDefinition(itemId, "Wand of Fireball"));
        var stack = new InventoryItemStackComponent(itemId, quantity: 1, overrideDefinition: CreateDefinition(itemId, "Wand of Fireball (5 charges)"));

        Assert.IsTrue(InventoryQueries.TryResolveEffectiveItem(catalog, in stack, out var definition));
        Assert.AreEqual("Wand of Fireball (5 charges)", definition.Name);
    }

    [TestMethod]
    public void TryResolveEffectiveItem_NoOverrideAndNotInCatalog_ReturnsFalse()
    {
        var catalog = new ItemCatalog();
        var stack = new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1);

        Assert.IsFalse(InventoryQueries.TryResolveEffectiveItem(catalog, in stack, out _));
    }

    [TestMethod]
    public void TryFindByStackInstanceId_MatchingStack_ReturnsTrueWithTheStack()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var target = new InventoryItemStackComponent(Guid.NewGuid(), quantity: 3);
        pool.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1)); // a decoy stack, must not match.
        pool.Add(0, target);

        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, 0, target.StackInstanceId, out var found));
        Assert.AreEqual(3, found.Quantity);
    }

    [TestMethod]
    public void TryFindByStackInstanceId_NoMatchingStack_ReturnsFalse()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(0, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));

        Assert.IsFalse(InventoryQueries.TryFindByStackInstanceId(pool, 0, Guid.NewGuid(), out _));
    }

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
    public void TryGetStack_MatchingItemDefinitionId_ReturnsTrueWithTheStack()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var potionId = Guid.NewGuid();
        pool.Add(0, new InventoryItemStackComponent(potionId, quantity: 5));

        Assert.IsTrue(InventoryQueries.TryGetStack(pool, 0, potionId, out var stack));
        Assert.AreEqual(5, stack.Quantity);
    }

    [TestMethod]
    public void TryGetStack_NoMatchingStack_ReturnsFalse()
    {
        var pool = new MultiComponentPool<InventoryItemStackComponent>(maximumEntityCount: 10, initialCapacity: 4);

        Assert.IsFalse(InventoryQueries.TryGetStack(pool, 0, Guid.NewGuid(), out _));
    }

    [TestMethod]
    public void IsInventoryDisabled_NoComponentPresent_DefaultsToFalse()
    {
        var pool = new PackedComponentPool<InventoryDisabledComponent>(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);

        Assert.IsFalse(InventoryQueries.IsInventoryDisabled(pool, 0));
    }

    [TestMethod]
    public void IsInventoryDisabled_ComponentPresentAndTrue_ReturnsTrue()
    {
        var pool = new PackedComponentPool<InventoryDisabledComponent>(maximumEntityCount: 10, initialCapacity: 10, static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);
        pool.Add(0, new InventoryDisabledComponent(isDisabled: true));

        Assert.IsTrue(InventoryQueries.IsInventoryDisabled(pool, 0));
    }
}
