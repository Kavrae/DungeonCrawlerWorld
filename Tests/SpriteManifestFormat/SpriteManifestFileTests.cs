using SpriteFormat;

namespace Tests.SpriteManifestFormat;

[TestClass]
public sealed class SpriteManifestFileTests
{
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
    public void Parse_ANameTwice_ThrowsNamingIt()
    {
        const string json = """
            [{"Name":"Twin","Cells":[{"SheetPath":"sheet.png","Column":0,"Row":0,"CellWidth":16,"CellHeight":16}]},
             {"Name":"Twin","Cells":[{"SheetPath":"sheet.png","Column":1,"Row":0,"CellWidth":16,"CellHeight":16}]}]
            """;

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Twin'");
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
