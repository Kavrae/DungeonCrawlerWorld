using SpriteFormat;
using UIEngine.UI.Filtering;
using Workshop.Sprites;

namespace Workshop.Tests;

/// <summary>How the Sprites tab narrows its entries: a search by name, Has problems exactly the entries the Error List names, and Kind.</summary>
[TestClass]
public sealed class SpriteEntryFiltersTests
{
    private static SpriteManifestCell Cell(int column = 0) => new("sheet.png", column, 0, 16, 16, 0);

    private static List<SpriteManifestCell?> CardinalSet() =>
        [.. Enumerable.Range(0, SpriteLinks.CellsPerSet(SpriteLinking.Cardinal)).Select(mask => mask == SpriteLinks.FallbackMask(SpriteLinking.Cardinal) ? Cell(5) : null)];

    private static IReadOnlyList<SpriteEntryView> Entries() => SpriteEntryView.Read(
    [
        new SpriteManifestEntry("Goblin", [Cell()]),
        new SpriteManifestEntry("Broken goblin", [Cell()], PixelsPerTile: 0),
        new SpriteManifestEntry("Rug", CardinalSet(), Linking: SpriteLinking.Cardinal),
        new SpriteManifestEntry("Stone wall", [Cell()], PixelsPerTile: 16, Top: new SpriteManifestPart(CardinalSet(), Linking: SpriteLinking.Cardinal)),
    ], _ => true);

    [TestMethod]
    public void TheSearch_MatchesANameAnywhere_IgnoringCase()
    {
        var entries = Entries();

        CollectionAssert.AreEqual(new[] { "Goblin", "Broken goblin" }, SpriteEntryFilters.Search(entries, "GOBLIN").Select(entry => entry.Name).ToArray());
        Assert.HasCount(entries.Count, SpriteEntryFilters.Search(entries, string.Empty));
    }

    [TestMethod]
    public void HasProblems_ListsExactlyTheEntriesWithProblems()
    {
        var entries = Entries();
        var filter = new ListFilter<SpriteEntryView>(SpriteEntryFilters.Groups);

        filter.Selection.SetCheckedIds([SpriteEntryFilters.HasProblemsChoiceId]);

        CollectionAssert.AreEqual(entries.Where(entry => entry.Problems.Count > 0).ToArray(), entries.Where(filter.Matches).ToArray());
        Assert.AreEqual("Broken goblin", entries.Where(filter.Matches).Single().Name);
    }

    [TestMethod]
    public void Kind_ListsEachKindChecked()
    {
        var entries = Entries();
        var filter = new ListFilter<SpriteEntryView>(SpriteEntryFilters.Groups);

        filter.Selection.SetCheckedIds(["kind:wall"]);
        Assert.AreEqual("Stone wall", entries.Where(filter.Matches).Single().Name);

        filter.Selection.SetCheckedIds(["kind:wall", "kind:linked-set"]);
        CollectionAssert.AreEquivalent(new[] { "Rug", "Stone wall" }, entries.Where(filter.Matches).Select(entry => entry.Name).ToArray());
    }

    [TestMethod]
    public void TheGroups_AreShowOnlyThenKind()
    {
        CollectionAssert.AreEqual(new[] { "Show only", "Kind: any of" }, SpriteEntryFilters.Groups.Select(group => group.Heading).ToArray());
        CollectionAssert.AreEqual(new[] { "Options", "Linked set", "Wall" }, SpriteEntryFilters.Groups[1].Choices.Select(choice => choice.Label).ToArray());
    }
}
