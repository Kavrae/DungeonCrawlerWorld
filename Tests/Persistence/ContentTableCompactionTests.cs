using Engine.Content;
using Engine.Persistence;

namespace Tests.Persistence;

/// <summary>Compacting a save's table: each Guid's last line kept where it first appeared, removals and superseded lines dropped, everything that decides nothing kept as written.</summary>
[TestClass]
public sealed class ContentTableCompactionTests
{
    private const string TableName = "Actions";

    private static readonly Guid FirstId = new("0c1e3a5b-7d9f-4b2d-8e6a-000000000001");
    private static readonly Guid SecondId = new("0c1e3a5b-7d9f-4b2d-8e6a-000000000002");
    private static readonly Guid ThirdId = new("0c1e3a5b-7d9f-4b2d-8e6a-000000000003");

    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory().FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private static string Line(Guid id, string name) => $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\"}}";

    private string WriteTable(params string[] recordLines)
    {
        var path = Path.Combine(_directory, "Actions.jsonl");
        File.WriteAllText(path, string.Join("\n", [ContentTable.Header(TableName), .. recordLines]) + "\n");
        return path;
    }

    private static string[] RecordLines(string path) => [.. File.ReadAllLines(path).Skip(1)];

    [TestMethod]
    public void EachGuidKeepsItsLastLine_WhereItFirstAppeared()
    {
        var path = WriteTable(Line(FirstId, "A1"), Line(SecondId, "B1"), Line(FirstId, "A2"), Line(SecondId, "B2"), Line(FirstId, "A3"));

        var dropped = ContentTableCompaction.Compact(path);

        Assert.AreEqual(3, dropped);
        CollectionAssert.AreEqual(new[] { Line(FirstId, "A3"), Line(SecondId, "B2") }, RecordLines(path));
        Assert.AreEqual(ContentTable.Header(TableName), File.ReadAllLines(path)[0]);
    }

    [TestMethod]
    public void AGuidWhoseLastLineIsARemoval_IsDroppedWhole_AndOneOverriddenAgainAfterItKeepsTheOverride()
    {
        var path = WriteTable(
            Line(FirstId, "A1"), ContentTable.WriteRemoval(FirstId),
            Line(SecondId, "B1"), ContentTable.WriteRemoval(SecondId), Line(SecondId, "B2"));

        var dropped = ContentTableCompaction.Compact(path);

        Assert.AreEqual(4, dropped);
        CollectionAssert.AreEqual(new[] { Line(SecondId, "B2") }, RecordLines(path));
    }

    [TestMethod]
    public void LinesThatDecideNothing_AreKeptAsWritten_AndACutShortLastLineIsDropped()
    {
        var disabled = $"{{\"id\":\"{ContentGuid.Format(ThirdId)}\",\"off\":true}}";
        var path = Path.Combine(_directory, "Actions.jsonl");
        File.WriteAllText(path, string.Join("\n", ContentTable.Header(TableName), "not json", disabled, Line(FirstId, "A1"), Line(FirstId, "A2")) + "\n{\"id\":\"cut");

        var dropped = ContentTableCompaction.Compact(path);

        Assert.AreEqual(2, dropped);
        CollectionAssert.AreEqual(new[] { "not json", disabled, Line(FirstId, "A2") }, RecordLines(path));
    }

    [TestMethod]
    public void ATableWithNothingToDrop_IsNotWritten()
    {
        var path = WriteTable(Line(FirstId, "A1"), Line(SecondId, "B1"));
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        Assert.AreEqual(0, ContentTableCompaction.Compact(path));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void ATableWhoseHeaderCantBeRead_IsLeftAlone()
    {
        var path = Path.Combine(_directory, "Actions.jsonl");
        var text = string.Join("\n", "{\"fmt\":99,\"table\":\"Actions\"}", Line(FirstId, "A1"), Line(FirstId, "A2")) + "\n";
        File.WriteAllText(path, text);

        Assert.AreEqual(0, ContentTableCompaction.Compact(path));
        Assert.AreEqual(text, File.ReadAllText(path));
    }

    [TestMethod]
    public void StartupCleanup_CompactsEveryTableOfTheSavesDefinitions()
    {
        using var save = SaveFolder.OpenOrCreate(_directory, "First");
        var path = save.DefinitionsPathOf("Actions.jsonl");
        save.Writer.Append(path, ContentTable.Header(TableName), Line(FirstId, "A1"));
        save.Writer.Append(path, ContentTable.Header(TableName), Line(FirstId, "A2"));

        var result = StartupCleanup.Run([], save);

        Assert.AreEqual(1, result.DefinitionLinesCompacted);
        CollectionAssert.AreEqual(new[] { Line(FirstId, "A2") }, RecordLines(path));
    }
}
