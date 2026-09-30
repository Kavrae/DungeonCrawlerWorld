using Engine.Math;
using Engine.Tags;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Lootboxes;

[TestClass]
public sealed class ItemContentsTests
{
    private static ItemDefinition Item(string name, params GameplayTag[] tags) =>
        new(Guid.NewGuid(), name, SpriteName: null, Glyph: "?", Color.White, Tags: GameplayTagSet.Create(tags), Effects: []);

    private static ItemCatalog CatalogWithPlainItemsAndABox(out ItemDefinition[] plainItems)
    {
        var itemCatalog = new ItemCatalog();
        plainItems = [Item("Potion", GameTags.ItemConsumablePotion), Item("Scroll", GameTags.ItemConsumableScroll), Item("Wand", GameTags.ItemWand)];
        foreach (var item in plainItems)
        {
            itemCatalog.Register(item);
        }

        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        lootboxCatalog.Register(LootboxTypes.Adventurer);
        lootboxCatalog.GetOrCreateItem(new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze));
        return itemCatalog;
    }

    [TestMethod]
    public void SetItemContents_WithEqualEntriesBuiltSeparately_AreEqual()
    {
        var itemId = Guid.NewGuid();

        var first = new SetItemContents([new ItemContentsEntry(itemId, 2), new ItemContentsEntry(Guid.Empty, 1)]);
        var second = new SetItemContents([new ItemContentsEntry(itemId, 2), new ItemContentsEntry(Guid.Empty, 1)]);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.AreNotEqual(first, new SetItemContents([new ItemContentsEntry(Guid.Empty, 1), new ItemContentsEntry(itemId, 2)]));
    }

    [TestMethod]
    public void SetItemContents_Roll_GrantsExactlyItsEntries()
    {
        var entries = new[] { new ItemContentsEntry(Guid.NewGuid(), 3) };

        var rolled = new SetItemContents(entries).Roll(new SeededRandom(1), new ItemCatalog());

        CollectionAssert.AreEqual(entries, rolled.ToArray());
    }

    [TestMethod]
    public void RandomSingleStackContents_Roll_GrantsOneStackOfOneToTenOfAPlainItem_NeverALootbox()
    {
        var itemCatalog = CatalogWithPlainItemsAndABox(out var plainItems);
        var plainItemIds = plainItems.Select(item => item.Id).ToHashSet();
        var rolls = new SeededRandom(7);

        for (var roll = 0; roll < 200; roll++)
        {
            var rolled = RandomSingleStackContents.Instance.Roll(rolls, itemCatalog);

            Assert.HasCount(1, rolled);
            Assert.Contains(rolled[0].ItemDefinitionId, plainItemIds);
            Assert.IsTrue(rolled[0].Quantity is >= RandomSingleStackContents.MinimumQuantity and <= RandomSingleStackContents.MaximumQuantity);
        }
    }

    [TestMethod]
    public void RandomSingleStackContents_Roll_SameSeed_GrantsTheSameItems()
    {
        var itemCatalog = CatalogWithPlainItemsAndABox(out _);

        var first = RandomSingleStackContents.Instance.Roll(new SeededRandom(42), itemCatalog);
        var second = RandomSingleStackContents.Instance.Roll(new SeededRandom(42), itemCatalog);

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
    }

    [TestMethod]
    public void RandomSingleStackContents_Roll_WithNoPlainItems_GrantsNothing()
    {
        Assert.IsEmpty(RandomSingleStackContents.Instance.Roll(new SeededRandom(1), new ItemCatalog()));
    }
}
