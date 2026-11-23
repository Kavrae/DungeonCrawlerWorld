using Engine.Content;
using Game.Content;

namespace Tests.GameTests.Content;

/// <summary>A table file edited one record at a time: every other line kept byte for byte, a new record at its Guid's place, and the master tables kept in that order.</summary>
[TestClass]
public sealed class ContentTableLinesTests
{
    private static readonly string[] MasterTableFiles = [GameContent.ActionsFileName, GameContent.ItemsFileName, GameContent.AurasFileName];

    private static string MasterText(string fileName) => File.ReadAllText(Path.Combine(GameContent.DefaultMasterDirectory, fileName));

    private static string Line(Guid id, string name) => $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\"}}";

    [TestMethod]
    public void ReplacingEachMasterLineWithItself_LeavesTheFileByteIdentical()
    {
        foreach (var fileName in MasterTableFiles)
        {
            var text = MasterText(fileName);
            var table = ContentTableLines.Parse(text);
            foreach (var line in table.Lines.ToList())
            {
                Assert.IsTrue(ContentTable.TryReadId(line, out var id), $"{fileName}: a line with no id");
                table.Put(id, line);
            }

            Assert.AreEqual(text, table.ToText(), fileName);
        }
    }

    [TestMethod]
    public void EveryMasterTable_IsInGuidOrder()
    {
        foreach (var fileName in MasterTableFiles)
        {
            var ids = ContentTableLines.Parse(MasterText(fileName)).Lines
                .Select(line => ContentTable.TryReadId(line, out var id) ? id : throw new AssertFailedException($"{fileName}: a line with no id"))
                .ToList();

            CollectionAssert.AreEqual(ids.Order(ContentGuid.FileOrder).ToList(), ids, fileName);
        }
    }

    [TestMethod]
    public void ANewRecord_LandsAtItsGuidsPlace_AndNoOtherLineChanges()
    {
        var text = MasterText(GameContent.ItemsFileName);
        var table = ContentTableLines.Parse(text);
        var before = table.Lines.ToList();
        var newId = Guid.NewGuid();

        table.Put(newId, Line(newId, "New"));

        var after = table.Lines.ToList();
        var insertedAt = after.FindIndex(line => ContentTable.TryReadId(line, out var id) && id == newId);
        Assert.HasCount(before.Count + 1, after);
        CollectionAssert.AreEqual(before, after.Where((_, index) => index != insertedAt).ToList());
        var ids = after.Select(line => ContentTable.TryReadId(line, out var id) ? id : Guid.Empty).ToList();
        CollectionAssert.AreEqual(ids.Order(ContentGuid.FileOrder).ToList(), ids);
    }

    [TestMethod]
    public void RemovingARecord_TakesOnlyItsLine_AndPuttingItBackRestoresTheFile()
    {
        var text = MasterText(GameContent.ActionsFileName);
        var table = ContentTableLines.Parse(text);
        var removed = table.Lines[table.Lines.Count / 2];
        Assert.IsTrue(ContentTable.TryReadId(removed, out var removedId));
        var others = table.Lines.Where(line => line != removed).ToList();

        Assert.IsTrue(table.Remove(removedId));
        CollectionAssert.AreEqual(others, table.Lines.ToList());
        Assert.IsFalse(table.Remove(removedId));

        table.Put(removedId, removed);
        Assert.AreEqual(text, table.ToText());
    }

    [TestMethod]
    public void PuttingARecordHeldOnSeveralLines_KeepsTheFirstPlace_AndDropsTheRest()
    {
        var first = new Guid("5b0f6f5e-6b8c-4b1e-9a51-000000000001");
        var second = new Guid("5b0f6f5e-6b8c-4b1e-9a51-000000000002");
        var table = ContentTableLines.Parse(string.Join('\n', ContentTable.Header("Named"), Line(first, "A"), Line(second, "B"), Line(first, "C")) + "\n");

        table.Put(first, Line(first, "D"));

        CollectionAssert.AreEqual(new[] { Line(first, "D"), Line(second, "B") }, table.Lines.ToList());
        Assert.IsTrue(table.TryGetLine(first, out var line));
        Assert.AreEqual(Line(first, "D"), line);
    }

    [TestMethod]
    public void PuttingALineThatHoldsAnotherGuid_IsRefused()
    {
        var table = ContentTableLines.Empty(ContentTable.Header("Named"));

        Assert.ThrowsExactly<ArgumentException>(() => table.Put(Guid.NewGuid(), Line(Guid.NewGuid(), "Other")));
    }

    [TestMethod]
    public void Writing_GoesThroughATemporaryFile_AndLeavesTheTextAsBuilt()
    {
        var directory = Directory.CreateTempSubdirectory("content-table-lines-");
        try
        {
            var path = Path.Combine(directory.FullName, GameContent.ItemsFileName);
            var table = ContentTableLines.Parse(MasterText(GameContent.ItemsFileName));

            table.Write(path);

            Assert.AreEqual(table.ToText(), File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
            Assert.AreNotEqual(0xEF, File.ReadAllBytes(path)[0]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ATableWithWindowsLineEndings_KeepsThem_OnEveryLineItPuts()
    {
        var text = MasterText(GameContent.ItemsFileName).Replace("\n", "\r\n", StringComparison.Ordinal);
        var table = ContentTableLines.Parse(text);
        var added = Guid.NewGuid();
        Assert.IsTrue(ContentTable.TryReadId(table.Lines[0], out var first));

        table.Put(first, table.Lines[0]);
        table.Put(added, Line(added, "Added"));

        var written = table.ToText();
        Assert.IsFalse(written.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'));
        Assert.Contains(Line(added, "Added") + "\r\n", written);
        Assert.AreEqual(text, ContentTableLines.Parse(text).ToText());
    }

    [TestMethod]
    public void AWriteThatFails_LeavesTheFileAsItWas_AndNoTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory("content-table-lines-");
        try
        {
            var path = Path.Combine(directory.FullName, GameContent.ItemsFileName);
            File.WriteAllText(path, "before");
            var table = ContentTableLines.Parse(MasterText(GameContent.ItemsFileName));

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<Exception>(() => table.Write(path));
            }

            Assert.AreEqual("before", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
