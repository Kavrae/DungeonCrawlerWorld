using System.Text.Json;
using Game.Blueprints;
using Microsoft.Xna.Framework;

namespace Tests.Blueprints;

/// <summary>
/// Content/SpriteManifest.json is meant to be edited through DevTools/SpriteManifestBuilder,
/// not treated as a frozen snapshot -- these tests verify TryGet's contract (a known name
/// resolves to one of its own entry's candidate cells, an unknown name doesn't) against
/// whichever cells the file actually holds, rather than asserting specific hardcoded
/// column/row values that a legitimate tool edit would immediately break.
/// </summary>
[TestClass]
public sealed class SpriteManifestTests
{
    private static List<SpriteManifestEntry> LoadRawEntries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];
    }

    [TestMethod]
    [DataRow("Wall")]
    [DataRow("Grass")]
    [DataRow("Player")]
    [DataRow("Goblin")]
    public void TryGet_KnownName_ReturnsOneOfItsCandidateCells(string name)
    {
        var entry = LoadRawEntries().Single(e => e.Name == name);

        var found = SpriteManifest.TryGet(name, out var sprite);

        Assert.IsTrue(found);
        var matchesACandidate = entry.Cells.Any(cell =>
            cell.SheetPath == sprite.SheetPath &&
            sprite.SourceRectangle == new Rectangle(cell.Column * cell.CellWidth, cell.Row * cell.CellHeight, cell.CellWidth, cell.CellHeight));
        Assert.IsTrue(matchesACandidate, $"TryGet(\"{name}\") returned {sprite.SheetPath} {sprite.SourceRectangle}, not one of that entry's own cells.");
    }

    [TestMethod]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        var found = SpriteManifest.TryGet("NoSuchName", out _);

        Assert.IsFalse(found);
    }
}
