using Builders.Items;
using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Modules.Actions.Activators;

namespace Workshop.Tests;

[TestClass]
public sealed class ItemBrowserSectionsTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(Path.Combine(AppContext.BaseDirectory, "NoMods"), [], TextWriter.Null));

    private static IReadOnlyList<ItemBrowserSection> Build(string search) =>
        ItemBrowserSections.Build(Session.Value.Catalogs.ItemCatalog.Definitions, search, Session.Value.Catalogs.GameplayTags);

    [TestMethod]
    public void EmptySearch_ListsEveryItemOnce()
    {
        var listed = Build(string.Empty).SelectMany(section => section.Items).ToList();

        Assert.HasCount(Session.Value.Catalogs.ItemCatalog.Count, listed);
        CollectionAssert.AllItemsAreUnique(listed);
    }

    [TestMethod]
    public void Sections_KeepTheirFixedOrder_AndSortByNameWithin()
    {
        string[] headingsInOrder = ["Potions", "Scrolls", "Wands", "Toggles", "Other"];
        var sections = Build(string.Empty);

        var headingIndexes = sections.Select(section => Array.IndexOf(headingsInOrder, section.Heading)).ToList();
        CollectionAssert.DoesNotContain(headingIndexes, -1);
        CollectionAssert.AreEqual(headingIndexes.Order().ToList(), headingIndexes);

        foreach (var section in sections)
        {
            var names = section.Items.Select(item => item.Name).ToList();
            CollectionAssert.AreEqual(names.Order(StringComparer.OrdinalIgnoreCase).ToList(), names, $"{section.Heading} is not sorted by name.");
        }
    }

    [TestMethod]
    public void Potions_HoldOnlyPotions()
    {
        var potions = Build(string.Empty).Single(section => section.Heading == "Potions");

        Assert.IsTrue(potions.Items.All(item => item.Activator is PotionActivator));
    }

    [TestMethod]
    public void Search_MatchesPartOfAName_IgnoringCase()
    {
        var anyItem = Session.Value.Catalogs.ItemCatalog.Definitions.First();
        var search = anyItem.Name[..Math.Min(4, anyItem.Name.Length)].ToUpperInvariant();

        var listed = Build(search).SelectMany(section => section.Items).ToList();

        CollectionAssert.Contains(listed, anyItem);
    }

    [TestMethod]
    public void Search_MatchesATagsDisplayName()
    {
        var gameplayTags = Session.Value.Catalogs.GameplayTags;
        var taggedItem = Session.Value.Catalogs.ItemCatalog.Definitions.First(item => item.Tags.Count > 0);
        var tagDisplayName = string.Empty;
        foreach (var tag in taggedItem.Tags)
        {
            tagDisplayName = gameplayTags.GetDisplayName(tag);
            break;
        }

        var listed = Build(tagDisplayName).SelectMany(section => section.Items).ToList();

        CollectionAssert.Contains(listed, taggedItem);
    }

    [TestMethod]
    public void Search_ThatMatchesNothing_LeavesNoSections()
    {
        Assert.IsEmpty(Build("zzzz-no-such-item-zzzz"));
    }
}
