using System.Text;
using System.Text.Json;
using Engine.Json;

namespace Tests.Json;

/// <summary>The engine's JSON plumbing: JSON Lines framing, the source map and the expanded layout.</summary>
[TestClass]
public sealed class JsonLinesTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public void Lines_AreNumberedAsTheFileHoldsThem_BlankOnesSkipped_AndEachNamesItsOwnBytes()
    {
        var bytes = Bytes("{\"a\":1}\r\n\n   \n{\"é\":2}\n");

        var lines = JsonLines.Split(bytes).ToList();

        CollectionAssert.AreEqual(new[] { 1, 4 }, lines.Select(line => line.Number).ToArray());
        Assert.AreEqual("{\"a\":1}", Encoding.UTF8.GetString(bytes, lines[0].Offset, lines[0].Length));
        Assert.AreEqual("{\"é\":2}", Encoding.UTF8.GetString(bytes, lines[1].Offset, lines[1].Length));
        Assert.IsTrue(lines.All(line => line.IsComplete));
    }

    [TestMethod]
    public void ALastLineCutShort_IsLeftOut_AndAnyOtherLineThatIsntJson_IsReported()
    {
        var results = JsonLines.Read(Bytes("{\"a\":1}\n{ broken\n{\"b\":2}\n{\"cut")).ToList();

        Assert.HasCount(3, results);
        Assert.IsNull(results[1].Document);
        Assert.AreEqual(2, results[1].Line.Number);
        Assert.IsFalse(string.IsNullOrEmpty(results[1].Problem));
        Assert.AreEqual(2, results[2].Document!.RootElement.GetProperty("b").GetInt32());
        foreach (var result in results)
        {
            result.Document?.Dispose();
        }
    }

    [TestMethod]
    public void TheSourceMap_NamesTheLineAndColumnOfEachValue_ByPath()
    {
        const string text = "{\n  \"rows\": [\n    \"ab\",\n    \"cd\"\n  ],\n  \"n\": 1\n}\n";

        var map = JsonSourceMap.Of(Bytes(text), "file.json");

        Assert.IsTrue(map.TryGetPosition("rows/1", out var line, out var column));
        Assert.AreEqual((4, 5), (line, column));
        Assert.AreEqual(6, map.LineOf("n"));
        Assert.AreEqual(2, map.LineOf("rows/9"));
    }

    [TestMethod]
    public void TheExpandedLayout_PutsAnObjectsFieldsAndAListsElementsOnePerLine_AndWhatIsInsideAListCompactly()
    {
        using var document = JsonDocument.Parse("{\"n\":\"é\",\"rows\":[\"ab\",\"cd\"],\"ent\":[{\"x\":1,\"y\":2}],\"none\":[]}");

        Assert.AreEqual("{\n  \"n\": \"é\",\n  \"rows\": [\n    \"ab\",\n    \"cd\"\n  ],\n  \"ent\": [\n    {\"x\":1,\"y\":2}\n  ],\n  \"none\": []\n}", JsonExpandedLayout.Write(document.RootElement));
    }
}
