using SpriteFormat;
using Workshop;
using Workshop.Sprites;

namespace Workshop.Tests;

[TestClass]
public sealed class SpritesTabTests
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    private static SpriteManifestCell Cell(int column = 0, int columnSpan = 0, string sheet = "sheet.png", string? tint = null) =>
        new(sheet, column, 0, 16, 16, columnSpan, Tint: tint);

    private static SpriteEntryView View(SpriteManifestEntry entry, Func<string, bool>? sheetExists = null) =>
        SpriteEntryView.Of(entry, sheetExists ?? (_ => true));

    private static List<SpriteManifestCell?> CardinalSet(SpriteManifestCell? fallback) =>
        [.. Enumerable.Range(0, SpriteLinks.CellsPerSet(SpriteLinking.Cardinal)).Select(mask => mask == SpriteLinks.FallbackMask(SpriteLinking.Cardinal) ? fallback : null)];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test assembly's location.");
    }

    [TestMethod]
    public void Options_AreOnePart()
    {
        var view = View(new SpriteManifestEntry("Goblin", [Cell(), Cell(1)]));

        Assert.AreEqual(SpriteEntryKind.Options, view.Kind);
        Assert.AreEqual(1, view.Parts.Count);
        Assert.IsNull(view.Parts[0].Layout);
        Assert.AreEqual(Cell(), view.Parts[0].PreviewCell);
    }

    [TestMethod]
    public void LinkedSet_PreviewsItsFallbackPiece()
    {
        var fallback = Cell(5);
        var view = View(new SpriteManifestEntry("Rug", CardinalSet(fallback), Linking: SpriteLinking.Cardinal));

        Assert.AreEqual(SpriteEntryKind.LinkedSet, view.Kind);
        Assert.AreSame(SpriteLinkLayout.Cardinal, view.Parts[0].Layout);
        Assert.AreEqual(fallback, view.Parts[0].PreviewCell);
    }

    /// <summary>A wall is its face then its top, the top sitting on the rows the face covers.</summary>
    [TestMethod]
    public void Wall_IsAFaceAndATop_TheTopRaisedByTheFacesHeight()
    {
        var face = new SpriteManifestEntry("Wall", [Cell()], PixelsPerTile: 16, Top: new SpriteManifestPart(CardinalSet(Cell(3)), Linking: SpriteLinking.Cardinal));

        var view = View(face);

        Assert.AreEqual(SpriteEntryKind.Wall, view.Kind);
        CollectionAssert.AreEqual(new[] { "Face", "Top" }, view.Parts.Select(part => part.Label).ToArray());
        Assert.AreEqual(face.Face.HeightRows(), view.Parts[1].RaisedRows);
        Assert.AreEqual(0, view.Parts[0].RaisedRows);
    }

    [TestMethod]
    public void SheetPaths_AreEachSheetOnce()
    {
        var view = View(new SpriteManifestEntry("Mixed", [Cell(sheet: "a.png"), Cell(1, sheet: "b.png"), Cell(2, sheet: "a.png")]));

        CollectionAssert.AreEqual(new[] { "a.png", "b.png" }, view.SheetPaths.ToArray());
    }

    [TestMethod]
    public void WhatTheGameRefuses_IsAnError()
    {
        var view = View(new SpriteManifestEntry("Bad", [Cell()], PixelsPerTile: 0));

        var problem = view.Problems.Single();
        Assert.AreEqual(WorkshopProblemSeverity.Error, problem.Severity);
        StringAssert.Contains(problem.Description, "PixelsPerTile");
        StringAssert.Contains(problem.Description, "draws its glyph");
    }

    [TestMethod]
    public void CellsThatDontFitTheSpan_AreAWarning()
    {
        var view = View(new SpriteManifestEntry("Uneven", [Cell(), Cell(1, columnSpan: 2)], ColumnSpan: 1, RowSpan: 1));

        var problem = view.Problems.Single();
        Assert.AreEqual(WorkshopProblemSeverity.Warning, problem.Severity);
        StringAssert.Contains(problem.Description, "1 cell(s)");
    }

    [TestMethod]
    public void AMissingSheet_IsAWarning_NamingIt()
    {
        var view = View(new SpriteManifestEntry("Lost", [Cell(sheet: "gone.png")]), sheetExists: path => path != "gone.png");

        var problem = view.Problems.Single();
        Assert.AreEqual(WorkshopProblemSeverity.Warning, problem.Severity);
        StringAssert.Contains(problem.Description, "gone.png");
    }

    [TestMethod]
    public void Id_IsStableForAName_AndDiffersBetweenNames()
    {
        Assert.AreEqual(SpriteEntryView.IdOf("Goblin"), View(new SpriteManifestEntry("Goblin", [Cell()])).Id);
        Assert.AreNotEqual(SpriteEntryView.IdOf("Goblin"), SpriteEntryView.IdOf("Orc"));
    }

    [TestMethod]
    public void RepeatedName_IsAnErrorOnTheLaterEntry_AndEachHasItsOwnId()
    {
        var views = SpriteEntryView.Read([new SpriteManifestEntry("Goblin", [Cell()]), new SpriteManifestEntry("Goblin", [Cell(1)])], _ => true);

        Assert.AreEqual(0, views[0].Problems.Count);
        var problem = views[1].Problems.Single();
        Assert.AreEqual(WorkshopProblemSeverity.Error, problem.Severity);
        StringAssert.Contains(problem.Description, "repeats the name");
        Assert.AreEqual(SpriteEntryView.IdOf("Goblin"), views[0].Id);
        Assert.AreNotEqual(views[0].Id, views[1].Id);
    }

    [TestMethod]
    public void RepeatedName_AfterAnEntryTheGameRefuses_IsTheOneLoaded()
    {
        var views = SpriteEntryView.Read([new SpriteManifestEntry("Goblin", [], Linking: SpriteLinking.Cardinal), new SpriteManifestEntry("Goblin", [Cell()])], _ => true);

        Assert.IsTrue(views[0].Problems.Any(problem => problem.Severity == WorkshopProblemSeverity.Error));
        Assert.AreEqual(0, views[1].Problems.Count);
    }

    [TestMethod]
    public void Summary_NamesTheKind_EachPart_ItsPlacement_TintsAndProblems()
    {
        var view = View(new SpriteManifestEntry("Chest", [Cell(tint: "#FF8800"), Cell(1, columnSpan: 2)], PixelsPerTile: 16, ColumnSpan: 1, RowSpan: 1));

        var summary = SpriteEntrySummary.Of(view);

        StringAssert.Contains(summary, "Options");
        StringAssert.Contains(summary, "2 option(s), span 1x1");
        StringAssert.Contains(summary, "16 px per tile, pivot at each cell's bottom-centre");
        StringAssert.Contains(summary, "Tinted #FF8800");
        StringAssert.Contains(summary, "Warning:");
    }

    /// <summary>Every entry of the shipped manifest reads and summarises, and none is one the game refuses.</summary>
    [TestMethod]
    public void ShippedManifest_ReadsWithNoErrors()
    {
        var content = Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Content");
        var entries = SpriteManifest.ReadEntries(Path.Combine(content, "SpriteManifest.json"));

        var views = SpriteEntryView.Read(entries, sheetPath => File.Exists(Path.Combine(content, "Spritesheets", sheetPath)));

        Assert.AreEqual(entries.Count, views.Count);
        Assert.IsTrue(views.All(view => SpriteEntrySummary.Of(view).Length > 0));
        var errors = views.SelectMany(view => view.Problems).Where(problem => problem.Severity == WorkshopProblemSeverity.Error).Select(problem => problem.Description).ToList();
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }
}
