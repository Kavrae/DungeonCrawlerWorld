using Engine.Content;
using Engine.Persistence;

namespace Tests.Persistence;

/// <summary>A save folder: its header, its layer, and the appender everything it holds is written through.</summary>
[TestClass]
public sealed class SaveFolderTests
{
    private static readonly Guid FirstId = new("0c1e3a5b-7d9f-4b2d-8e6a-000000000001");
    private static readonly Guid SecondId = new("0c1e3a5b-7d9f-4b2d-8e6a-000000000002");

    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory().FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private static string Line(Guid id, string name) => $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\"}}";

    [TestMethod]
    public void ANewSave_HasItsFoldersAndNoHeader_AndItsDefinitionsAreASaveLayer()
    {
        using var save = SaveFolder.OpenOrCreate(_directory, "First");

        Assert.IsTrue(Directory.Exists(save.DefinitionsDirectory));
        Assert.IsTrue(Directory.Exists(save.DraftsDirectory));
        Assert.IsNull(save.Header);
        Assert.IsNull(save.HeaderProblem);
        Assert.AreEqual(ContentLayerKind.Save, save.DefinitionsLayer.Kind);
        Assert.AreEqual(save.DefinitionsDirectory, save.DefinitionsLayer.Directory);
    }

    [TestMethod]
    public void AHeader_IsReadBackWhenTheSaveIsOpenedAgain_AndOnlyContentDifferencesCount()
    {
        var header = new SaveHeader(SaveHeader.CurrentFormatVersion, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), 42, "abc", new Dictionary<string, int> { ["Core"] = 1, ["Inventory"] = 2 });
        using (var save = SaveFolder.OpenOrCreate(_directory, "First"))
        {
            save.WriteHeader(header);
        }

        using var reopened = SaveFolder.OpenOrCreate(_directory, "First");

        var read = reopened.Header!;
        Assert.AreEqual(header.CreatedUtc, read.CreatedUtc);
        Assert.AreEqual(42, read.Seed);
        CollectionAssert.AreEquivalent(header.ModuleContentVersions.ToArray(), read.ModuleContentVersions.ToArray());
        Assert.IsEmpty(read.DifferencesFrom(header with { Seed = 7, CreatedUtc = DateTimeOffset.UtcNow }));

        var differences = read.DifferencesFrom(header with { MasterContentHash = "def", ModuleContentVersions = new Dictionary<string, int> { ["Core"] = 1, ["Inventory"] = 3, ["Mod"] = 1 } });
        Assert.HasCount(3, differences);
    }

    [TestMethod]
    public void AnUnreadableHeader_IsReported_NotThrown()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Broken"));
        File.WriteAllText(Path.Combine(_directory, "Broken", SaveFolder.HeaderFileName), "{ not json");

        using var save = SaveFolder.OpenOrCreate(_directory, "Broken");

        Assert.IsNull(save.Header);
        Assert.IsNotNull(save.HeaderProblem);
    }

    [TestMethod]
    public void SaveNames_ThatNoFolderCanHave_AreRefused()
    {
        Assert.IsTrue(SaveFolder.IsValidName("20261103-221904-seed42"));
        Assert.IsFalse(SaveFolder.IsValidName(" "));
        Assert.IsFalse(SaveFolder.IsValidName(".."));
        Assert.IsFalse(SaveFolder.IsValidName("a/b"));
        Assert.ThrowsExactly<ArgumentException>(() => SaveFolder.OpenOrCreate(_directory, "a/b"));
        Assert.AreEqual("my save", SaveArgument.Parse(["--seed=1", SaveArgument.FormatArgument("my save")]));
        Assert.IsNull(SaveArgument.Parse(["--seed=1"]));
    }

    [TestMethod]
    public void TheAppender_WritesTheHeaderFirst_AndDisposingIt_FlushesEveryLine()
    {
        var path = Path.Combine(_directory, "Named.jsonl");
        var appender = new ContentTableAppender();
        for (var index = 0; index < 100; index++)
        {
            appender.Append(path, "{\"fmt\":1,\"table\":\"Named\"}", Line(FirstId, $"Line {index}"));
        }

        appender.Dispose();

        var lines = File.ReadAllLines(path);
        Assert.HasCount(101, lines);
        Assert.AreEqual("{\"fmt\":1,\"table\":\"Named\"}", lines[0]);
        StringAssert.Contains(lines[100], "Line 99");
    }

    [TestMethod]
    public void AppendingAfterALineCutShort_KeepsTheBrokenLineApart_SoTheRestStillLoads()
    {
        var path = Path.Combine(_directory, "Named.jsonl");
        File.WriteAllText(path, "{\"fmt\":1,\"table\":\"Named\"}\n" + Line(FirstId, "First") + "\n{\"id\":\"cut sh");
        using (var appender = new ContentTableAppender())
        {
            appender.Append(path, "unused", Line(SecondId, "Second"));
        }

        var lines = File.ReadAllLines(path);
        Assert.HasCount(4, lines);
        Assert.AreEqual(Line(SecondId, "Second"), lines[3]);
    }

    [TestMethod]
    public void RemovingARecord_TakesOutEveryLineHoldingItsGuid_AndKeepsTheRestAsWritten()
    {
        var path = Path.Combine(_directory, "Named.jsonl");
        const string brokenLine = "{ not a record";
        File.WriteAllText(path, string.Join('\n', "{\"fmt\":1,\"table\":\"Named\"}", Line(FirstId, "First"), brokenLine, Line(SecondId, "Second"), Line(FirstId, "First again")) + "\n");
        using (var appender = new ContentTableAppender())
        {
            appender.RemoveRecords(path, FirstId);
        }

        CollectionAssert.AreEqual(new[] { "{\"fmt\":1,\"table\":\"Named\"}", brokenLine, Line(SecondId, "Second") }, File.ReadAllLines(path));
    }

    [TestMethod]
    public void Flush_WaitsForEverythingQueuedBeforeIt()
    {
        var path = Path.Combine(_directory, "Named.jsonl");
        using var appender = new ContentTableAppender();
        appender.Append(path, "{\"fmt\":1,\"table\":\"Named\"}", Line(FirstId, "First"));

        appender.Flush();

        Assert.HasCount(2, File.ReadAllLines(path));
    }
}
