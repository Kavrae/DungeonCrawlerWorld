using Builders.Browsing;
using Builders.Items;
using Game.Bootstrap;
using Game.Modules.Inventory;
using UIEngine.UI.Filtering;

namespace Workshop.Tests;

/// <summary>The groups every browser shares: Source (master and each mod that supplied a record) and a group over a fixed set of values, over the built-in items.</summary>
[TestClass]
public sealed class RecordFilterGroupsTests
{
    private static readonly Lazy<GameSession> Session = new(() => DungeonCrawlerWorld.Workshop.ContentSession.Build(DungeonCrawlerWorld.Workshop.ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private static IReadOnlyList<ItemDefinition> Items => [.. Session.Value.Catalogs.ItemCatalog.Definitions];

    private static string? ModOf(ItemDefinition item) => item.Name switch
    {
        "Health Potion" => "ExampleMod",
        "Wand of Fireball" => "Another Mod",
        _ => null,
    };

    [TestMethod]
    public void Source_OffersMaster_ThenEachModThatSuppliedARecord_ByName()
    {
        var group = RecordFilterGroups.Source(Items, ModOf);

        Assert.AreEqual(ListFilterGroupMatch.AnyOf, group.Match);
        Assert.AreEqual("Source: any of", group.Heading);
        CollectionAssert.AreEqual(new[] { RecordFilterGroups.MasterLabel, "Another Mod", "ExampleMod" }, group.Choices.Select(choice => choice.Label).ToArray());
        CollectionAssert.AreEqual(new[] { RecordFilterGroups.MasterChoiceId, "source:mod:Another Mod", "source:mod:ExampleMod" }, group.Choices.Select(choice => choice.Id).ToArray());
    }

    [TestMethod]
    public void MasterAndAMod_ListBoth_AndAModAloneListsOnlyItsOwn()
    {
        var filter = new ListFilter<ItemDefinition>([RecordFilterGroups.Source(Items, ModOf)]);

        filter.Selection.SetCheckedIds([RecordFilterGroups.ModChoiceId("ExampleMod")]);
        Assert.AreEqual("Health Potion", Items.Where(filter.Matches).Single().Name);

        filter.Selection.SetCheckedIds([RecordFilterGroups.MasterChoiceId, RecordFilterGroups.ModChoiceId("ExampleMod")]);
        Assert.AreEqual(Items.Count - 1, filter.CountMatching(Items));
    }

    [TestMethod]
    public void NoModSuppliedAnything_OffersMasterAlone()
    {
        var group = RecordFilterGroups.Source(Items, static _ => null);

        Assert.AreEqual(RecordFilterGroups.MasterChoiceId, group.Choices.Single().Id);
    }

    [TestMethod]
    public void TheItemsTypeGroup_ListsExactlyTheBrowsersSections()
    {
        var filter = new ListFilter<ItemDefinition>([RecordFilterGroups.OneOf<ItemDefinition>("type", "Type", ItemBrowserSections.HeadingsInOrder, ItemBrowserSections.HeadingOf)]);
        var sections = ItemBrowserSections.Build(Items, string.Empty, Session.Value.Catalogs.GameplayTags);

        filter.Selection.SetCheckedIds(["type:potions", "type:scrolls"]);

        var expected = sections.Where(section => section.Heading is "Potions" or "Scrolls").SelectMany(section => section.Records).ToList();
        CollectionAssert.AreEquivalent(expected, Items.Where(filter.Matches).ToList());
        Assert.IsNotEmpty(expected);
        Assert.AreEqual("Type: any of", filter.Groups[0].Heading);
    }
}
