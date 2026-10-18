using SpriteFormat;

namespace Tests.SpriteManifestFormat;

[TestClass]
public sealed class SpriteManifestFileTests
{
    /// <summary>The one problem parsing reported, for an entry it left out.</summary>
    private static string SingleProblem(SpriteManifest manifest)
    {
        Assert.HasCount(1, manifest.Problems);
        return manifest.Problems[0];
    }

    private static string CheckedInManifestPath => Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json");

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    /// <summary>The builder reads and writes through the same model the game loads: re-saving the checked-in file unchanged must not change a byte.</summary>
    [TestMethod]
    public void Serialize_TheCheckedInManifest_WritesItBackUnchanged()
    {
        var json = File.ReadAllText(CheckedInManifestPath);

        var rewritten = SpriteManifest.Serialize(SpriteManifest.ReadEntries(CheckedInManifestPath));

        Assert.AreEqual(NormalizeLineEndings(json).TrimEnd(), NormalizeLineEndings(rewritten).TrimEnd());
    }

    [TestMethod]
    public void Load_TheCheckedInManifest_KeepsFileOrder()
    {
        var entries = SpriteManifest.ReadEntries(CheckedInManifestPath);

        var manifest = SpriteManifest.Load(CheckedInManifestPath);

        CollectionAssert.AreEqual(entries.Select(entry => entry.Name).ToList(), manifest.Entries.Select(entry => entry.Name).ToList());
    }

    [TestMethod]
    public void Load_TheCheckedInManifest_ReportsNoProblems()
    {
        Assert.IsEmpty(SpriteManifest.Load(CheckedInManifestPath).Problems);
    }

    [TestMethod]
    public void Parse_ABrokenEntryBetweenTwoGoodOnes_LoadsTheGoodOnes()
    {
        const string json = """
            [{"Name":"First","Cells":[{"SheetPath":"sheet.png","Column":0,"Row":0,"CellWidth":16,"CellHeight":16}]},
             {"Name":"Broken","Cells":[{"SheetPath":"sheet.png","Column":1,"Row":0,"CellWidth":16,"CellHeight":16,"Rotation":45}]},
             {"Name":"Last","Cells":[{"SheetPath":"sheet.png","Column":2,"Row":0,"CellWidth":16,"CellHeight":16}]}]
            """;

        var manifest = SpriteManifest.Parse(json);

        CollectionAssert.AreEqual(new[] { "First", "Last" }, manifest.Entries.Select(entry => entry.Name).ToArray());
        StringAssert.Contains(SingleProblem(manifest), "'Broken'");
    }

    [TestMethod]
    public void Write_ReplacesTheFile_AndLeavesNoTemporaryFileBehind()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "SpriteManifest.json");
            File.WriteAllText(path, "old");

            SpriteManifest.Write(path, [new SpriteManifestEntry("Written", [new SpriteManifestCell("sheet.png", 0, 0, 16, 16)])]);

            Assert.AreEqual("Written", SpriteManifest.Load(path).Entries.Single().Name);
            CollectionAssert.AreEqual(new[] { "SpriteManifest.json" }, Directory.GetFiles(directory.FullName).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Parse_ANameTwice_KeepsTheFirst_AndReportsTheSecondNamingIt()
    {
        const string json = """
            [{"Name":"Twin","Cells":[{"SheetPath":"sheet.png","Column":0,"Row":0,"CellWidth":16,"CellHeight":16}]},
             {"Name":"Twin","Cells":[{"SheetPath":"sheet.png","Column":1,"Row":0,"CellWidth":16,"CellHeight":16}]}]
            """;

        var manifest = SpriteManifest.Parse(json);
        var problem = SingleProblem(manifest);

        StringAssert.Contains(problem, "'Twin'");
        Assert.AreEqual(0, manifest.Entries.Single().Cells[0]!.Column);
    }

    [TestMethod]
    public void ReadEntries_NoFile_IsEmpty()
    {
        Assert.IsEmpty(SpriteManifest.ReadEntries(Path.Combine(AppContext.BaseDirectory, "NoSuchManifest.json")));
    }

    [TestMethod]
    public void TryGetEntry_AnAbsentName_IsAMiss()
    {
        var manifest = SpriteManifest.Load(CheckedInManifestPath);

        Assert.IsFalse(manifest.TryGetEntry("No such sprite", out _));
    }

    /// <summary>The format library is shared by the game, the tests and a WinForms tool, so it may depend on nothing but the runtime.</summary>
    [TestMethod]
    public void SpriteFormat_ReferencesOnlyTheRuntime()
    {
        var references = typeof(SpriteManifest).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToList();

        var foreign = references.Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard" && name != "mscorlib").ToList();

        Assert.IsEmpty(foreign, $"SpriteFormat references {string.Join(", ", foreign)}.");
    }

    /// <summary>Game only names sprites: what a name draws as is Presentation's, so Game never sees the manifest.</summary>
    [TestMethod]
    public void Game_DoesNotReferenceSpriteFormat()
    {
        var references = typeof(Game.World.World).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToList();

        CollectionAssert.DoesNotContain(references, typeof(SpriteManifest).Assembly.GetName().Name);
    }

    /// <summary>The footprint cap is Game's rule and the reach box the sprite format's; a Huge footprint fills the box exactly, so the two must agree.</summary>
    [TestMethod]
    public void MaximumFootprintTiles_FillsTheReachBoxsRowsAbove()
    {
        Assert.AreEqual(SpriteReach.MaximumRowsAbove + 1, Game.Blueprints.ResolvedBlueprint.MaximumFootprintTiles);
    }
}
