using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Tags;

namespace Tests.Modules.Lootboxes;

[TestClass]
public sealed class LootboxCatalogTests
{
    private static (ItemCatalog ItemCatalog, LootboxCatalog LootboxCatalog) CreateCatalogs()
    {
        var itemCatalog = new ItemCatalog();
        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        itemCatalog.AddDefinitionSource(lootboxCatalog);

        foreach (var type in LootboxTypes.All)
        {
            lootboxCatalog.Register(type);
        }

        return (itemCatalog, lootboxCatalog);
    }

    [TestMethod]
    public void NoItemDefinitionIsRegisteredUntilItsKindIsAskedFor()
    {
        var (itemCatalog, lootboxCatalog) = CreateCatalogs();

        Assert.AreEqual(0, itemCatalog.Count);

        lootboxCatalog.GetOrCreateItem(new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze));

        Assert.AreEqual(1, itemCatalog.Count);
    }

    [TestMethod]
    public void GetOrCreateItem_SameKindTwice_ReturnsTheSameDefinition()
    {
        var (_, lootboxCatalog) = CreateCatalogs();
        var kind = new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Silver);

        Assert.AreSame(lootboxCatalog.GetOrCreateItem(kind), lootboxCatalog.GetOrCreateItem(kind));
    }

    [TestMethod]
    public void ItemIdFor_IsTheSameInEveryCatalogAndDiffersByTypeAndRarity()
    {
        var bronzeAdventurer = new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze);

        Assert.AreEqual(CreateCatalogs().LootboxCatalog.GetOrCreateItem(bronzeAdventurer).Id, CreateCatalogs().LootboxCatalog.GetOrCreateItem(bronzeAdventurer).Id);
        Assert.AreNotEqual(LootboxCatalog.ItemIdFor(bronzeAdventurer), LootboxCatalog.ItemIdFor(bronzeAdventurer with { Rarity = LootboxRarity.Silver }));
        Assert.AreNotEqual(LootboxCatalog.ItemIdFor(bronzeAdventurer), LootboxCatalog.ItemIdFor(bronzeAdventurer with { TypeId = LootboxTypes.Weapon.Id }));
    }

    [TestMethod]
    public void CreatedDefinition_IsNamedByRarityAndTypeAndTaggedLootbox()
    {
        var (_, lootboxCatalog) = CreateCatalogs();

        var definition = lootboxCatalog.GetOrCreateItem(new LootboxKind(LootboxTypes.Alchemist.Id, LootboxRarity.Gold));

        Assert.AreEqual("Gold Alchemist Box", definition.Name);
        Assert.AreEqual<Engine.Tags.GameplayTagSet>([GameTags.ItemLootbox], definition.Tags);
        Assert.AreEqual(RandomSingleStackContents.Instance, definition.Contents);
    }

    [TestMethod]
    public void ItemCatalogLookup_OfAKindNothingCreatedYet_CreatesItThroughTheLootboxCatalog()
    {
        var (itemCatalog, _) = CreateCatalogs();
        var kind = new LootboxKind(LootboxTypes.Exorcist.Id, LootboxRarity.Celestial);

        Assert.IsTrue(itemCatalog.TryGet(LootboxCatalog.ItemIdFor(kind), out var definition));
        Assert.AreEqual("Celestial Exorcist Box", definition.Name);
    }

    [TestMethod]
    public void ItemCatalogLookup_OfAnUnknownId_IsStillAMiss()
    {
        var (itemCatalog, _) = CreateCatalogs();

        Assert.IsFalse(itemCatalog.TryGet(Guid.NewGuid(), out _));
    }

    [TestMethod]
    public void ItemCatalogLookup_OfATypeRegisteredAfterAnEarlierLookup_StillResolves()
    {
        var (itemCatalog, lootboxCatalog) = CreateCatalogs();
        itemCatalog.TryGet(Guid.NewGuid(), out _);

        var modType = new LootboxTypeDefinition(Guid.NewGuid(), "Modded");
        lootboxCatalog.Register(modType);

        Assert.IsTrue(itemCatalog.TryGet(LootboxCatalog.ItemIdFor(new LootboxKind(modType.Id, LootboxRarity.Bronze)), out var definition));
        Assert.AreEqual("Bronze Modded Box", definition.Name);
    }

    [TestMethod]
    public void RegisteringATypeWithABuiltInsId_ReplacesItsName()
    {
        var (_, lootboxCatalog) = CreateCatalogs();

        lootboxCatalog.Register(LootboxTypes.Adventurer with { Name = "Explorer" });

        Assert.AreEqual("Bronze Explorer Box", lootboxCatalog.DisplayName(new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze)));
    }

    [TestMethod]
    public void GetOrCreateItem_OfAnUnregisteredType_Throws()
    {
        var (_, lootboxCatalog) = CreateCatalogs();

        Assert.ThrowsExactly<InvalidOperationException>(() => lootboxCatalog.GetOrCreateItem(new LootboxKind(Guid.NewGuid(), LootboxRarity.Bronze)));
    }

    [TestMethod]
    public void GetOrCreateItem_OfARewardWithContents_SharesOneOverrideAcrossEqualContents()
    {
        var (_, lootboxCatalog) = CreateCatalogs();
        var itemId = Guid.NewGuid();

        var first = lootboxCatalog.GetOrCreateItem(new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, new SetItemContents([new ItemContentsEntry(itemId, 1)])));
        var second = lootboxCatalog.GetOrCreateItem(new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Gold, new SetItemContents([new ItemContentsEntry(itemId, 1)])));

        Assert.AreSame(first, second);
        Assert.AreEqual(LootboxCatalog.ItemIdFor(new LootboxKind(LootboxTypes.Boss.Id, LootboxRarity.Gold)), first.Id);
        Assert.AreEqual(new SetItemContents([new ItemContentsEntry(itemId, 1)]), first.Contents);
    }

    [TestMethod]
    public void TryGetKind_OfACreatedBox_ReturnsItsKind_AndOfAnyOtherItem_ReturnsFalse()
    {
        var (_, lootboxCatalog) = CreateCatalogs();
        var kind = new LootboxKind(LootboxTypes.Librarian.Id, LootboxRarity.Platinum);
        var definition = lootboxCatalog.GetOrCreateItem(kind);

        Assert.IsTrue(lootboxCatalog.TryGetKind(definition.Id, out var found));
        Assert.AreEqual(kind, found);
        Assert.IsFalse(lootboxCatalog.TryGetKind(Guid.NewGuid(), out _));
    }
}
