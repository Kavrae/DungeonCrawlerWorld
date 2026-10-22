using Builders.Actions;
using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Modules.Actions.Activators;

namespace Workshop.Tests;

[TestClass]
public sealed class ActionBrowserSectionsTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), []), [], TextWriter.Null));

    private static IReadOnlyList<Builders.Browsing.RecordBrowserSection<Game.Modules.Actions.ActionDefinition>> Build(string search) =>
        ActionBrowserSections.Build(Session.Value.Catalogs.ActionCatalog.Definitions, search, Session.Value.Catalogs.GameplayTags);

    [TestMethod]
    public void EmptySearch_ListsEveryActionOnce()
    {
        var listed = Build(string.Empty).SelectMany(section => section.Records).ToList();

        Assert.HasCount(Session.Value.Catalogs.ActionCatalog.Count, listed);
        CollectionAssert.AllItemsAreUnique(listed);
    }

    [TestMethod]
    public void Sections_KeepTheirFixedOrder_AndSortByNameWithin()
    {
        var headingsInOrder = ActionBrowserSections.HeadingsInOrder.ToList();
        var sections = Build(string.Empty);

        var headingIndexes = sections.Select(section => headingsInOrder.IndexOf(section.Heading)).ToList();
        CollectionAssert.DoesNotContain(headingIndexes, -1);
        CollectionAssert.AreEqual(headingIndexes.Order().ToList(), headingIndexes);

        foreach (var section in sections)
        {
            var names = section.Records.Select(action => action.Name).ToList();
            CollectionAssert.AreEqual(names.Order(StringComparer.OrdinalIgnoreCase).ToList(), names, $"{section.Heading} is not sorted by name.");
        }
    }

    [TestMethod]
    public void Toggles_HoldEveryToggleAction_AndNothingElse()
    {
        var sections = Build(string.Empty);
        var toggles = sections.Single(section => section.Heading == "Toggles").Records;

        Assert.IsTrue(toggles.All(action => action.Toggle is not null));
        Assert.IsTrue(sections.Where(section => section.Heading != "Toggles").SelectMany(section => section.Records).All(action => action.Toggle is null));
    }

    [TestMethod]
    public void Spells_HoldOnlyNonToggleSpells()
    {
        var spells = Build(string.Empty).Single(section => section.Heading == "Spells").Records;

        Assert.IsTrue(spells.All(action => action.Activator is SpellActivator && action.Toggle is null));
    }

    [TestMethod]
    public void Search_MatchesPartOfAName_IgnoringCase()
    {
        var anyAction = Session.Value.Catalogs.ActionCatalog.Definitions.First();
        var search = anyAction.Name[..Math.Min(4, anyAction.Name.Length)].ToLowerInvariant();

        CollectionAssert.Contains(Build(search).SelectMany(section => section.Records).ToList(), anyAction);
    }
}
