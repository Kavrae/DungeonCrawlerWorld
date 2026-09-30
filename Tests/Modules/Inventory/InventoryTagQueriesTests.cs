using Engine.ECS.Components;
using Engine.Tags;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class InventoryTagQueriesTests
{
    private const int EntityId = 0;

    private static (ComponentManager ComponentManager, ItemCatalog ItemCatalog) Build(params ItemDefinition[] heldItems)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var itemCatalog = new ItemCatalog();
        foreach (var item in heldItems)
        {
            itemCatalog.Register(item);
            InventoryActions.AddItem(componentManager, EntityId, item.Id, quantity: 1);
        }

        return (componentManager, itemCatalog);
    }

    private static List<(GameplayTag Tag, int Count)> Counts(params ItemDefinition[] heldItems)
    {
        var (componentManager, itemCatalog) = Build(heldItems);
        return InventoryTagQueries.GetItemCategoryCounts(componentManager, itemCatalog, TestGameplayTags.BuiltIn, EntityId);
    }

    [TestMethod]
    public void AnItem_CountsUnderItsCategoryAndEveryParentBelowItem()
    {
        var counts = Counts(HealthPotion.Build());

        CollectionAssert.AreEquivalent(
            new[] { (GameTags.ItemConsumable, 1), (GameTags.ItemConsumablePotion, 1) },
            counts.ToArray());
    }

    [TestMethod]
    public void TagsOutsideItem_AreNotCategories()
    {
        var counts = Counts(HealthPotion.Build(), WandOfFireball.Build());

        Assert.IsFalse(counts.Any(entry => !entry.Tag.IsSelfOrDescendantOf(GameTags.Item)), $"Only Item.* categories expected, got {string.Join(", ", counts)}.");
        Assert.IsFalse(counts.Any(entry => entry.Tag == GameTags.Item), "Item itself is never a tab.");
    }

    [TestMethod]
    public void AStack_CountsOnceUnderACategory_EvenWhenSeveralOfItsTagsLeadThere()
    {
        var counts = Counts(WandOfFireball.Build());

        Assert.AreEqual(1, counts.Single(entry => entry.Tag == GameTags.ItemConsumable).Count, "The wand declares Consumable and is not a Potion or Scroll, so Consumable counts it once.");
        Assert.AreEqual(1, counts.Single(entry => entry.Tag == GameTags.ItemWand).Count);
    }

    [TestMethod]
    public void MostPopulatedCategory_ComesFirst_ThenByDisplayName()
    {
        var counts = Counts(HealthPotion.Build(), ManaPotion.Build(), ScrollOfHealing.Build());

        CollectionAssert.AreEqual(
            new[] { GameTags.ItemConsumable, GameTags.ItemConsumablePotion, GameTags.ItemConsumableScroll },
            counts.Select(entry => entry.Tag).ToArray());
        Assert.AreEqual(3, counts[0].Count);
        Assert.AreEqual(2, counts[1].Count);
        Assert.AreEqual(1, counts[2].Count);
    }

    [TestMethod]
    public void AnItemWithNoItemTags_GetsNoCategory()
    {
        var untagged = new ItemDefinition(Guid.NewGuid(), "Pebble", null, "o", Color.Gray, Tags: [GameTags.TargetingSelf], Effects: []);

        Assert.IsEmpty(Counts(untagged));
    }
}
