using System.Text.Json;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Tests.Blueprints;

/// <summary>
/// Content/SpriteManifest.json is meant to be edited through DevTools/SpriteManifestBuilder,
/// not treated as a frozen snapshot -- these tests verify the two lookup paths' contracts (a
/// known name resolves to one of its own entry's candidate cells, or specifically to its first
/// one; an unknown name doesn't resolve at all) against whichever cells the file actually holds,
/// rather than asserting specific hardcoded column/row values that a legitimate tool edit would
/// immediately break.
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

    private static bool Matches(SpriteComponent sprite, SpriteManifestCell cell) =>
        cell.SheetPath == sprite.SheetPath &&
        sprite.SourceRectangle == new Rectangle(cell.Column * cell.CellWidth, cell.Row * cell.CellHeight, cell.CellWidth, cell.CellHeight);

    [TestMethod]
    [DataRow("Wall")]
    [DataRow("Grass")]
    [DataRow("Player")]
    [DataRow("Goblin")]
    public void TryGetRandom_KnownName_ReturnsOneOfItsCandidateCells(string name)
    {
        var entry = LoadRawEntries().Single(e => e.Name == name);

        var found = SpriteManifest.TryGetRandom(name, new MathUtility(new Random(1)), out var sprite);

        Assert.IsTrue(found);
        Assert.IsTrue(entry.Cells.Any(cell => Matches(sprite, cell)), $"TryGetRandom(\"{name}\") returned {sprite.SheetPath} {sprite.SourceRectangle}, not one of that entry's own cells.");
    }

    [TestMethod]
    [DataRow("Wall")]
    [DataRow("Grass")]
    [DataRow("Player")]
    [DataRow("Goblin")]
    public void TryGetFirst_KnownName_ReturnsThatEntrysFirstCell(string name)
    {
        var entry = LoadRawEntries().Single(e => e.Name == name);

        var found = SpriteManifest.TryGetFirst(name, out var sprite);

        Assert.IsTrue(found);
        Assert.IsTrue(Matches(sprite, entry.Cells[0]), $"TryGetFirst(\"{name}\") returned {sprite.SheetPath} {sprite.SourceRectangle}, not that entry's first cell.");
    }

    /// <summary>
    /// The whole point of the first-cell path: UI icons and map badges re-resolve by name on every
    /// frame, so repeated calls must return the identical sprite no matter how many candidate cells
    /// the entry grows to hold. Runs against every name in the file rather than a chosen few, so a
    /// multi-cell entry added later is covered without editing this test.
    /// </summary>
    [TestMethod]
    public void TryGetFirst_RepeatedCalls_ReturnTheSameSprite()
    {
        foreach (var entry in LoadRawEntries())
        {
            Assert.IsTrue(SpriteManifest.TryGetFirst(entry.Name, out var first));

            for (var attempt = 0; attempt < 20; attempt++)
            {
                Assert.IsTrue(SpriteManifest.TryGetFirst(entry.Name, out var again));
                Assert.AreEqual(first.SheetPath, again.SheetPath, $"TryGetFirst(\"{entry.Name}\") varied between calls.");
                Assert.AreEqual(first.SourceRectangle, again.SourceRectangle, $"TryGetFirst(\"{entry.Name}\") varied between calls.");
            }
        }
    }

    /// <summary>
    /// The reason TryGetRandom takes a MathUtility at all: variant selection has to fall under the
    /// session seed, so two players given the same seed see the same map built out of the same
    /// tiles rather than merely the same layout. Runs against every multi-cell entry in the file,
    /// so this keeps holding as the manifest grows -- and is skipped entirely when no entry has a
    /// choice to make, since a single-cell entry would pass no matter how badly seeded.
    /// </summary>
    [TestMethod]
    public void TryGetRandom_SameSeed_ResolvesToTheSameCellEveryTime()
    {
        var multiCellNames = LoadRawEntries().Where(entry => entry.Cells.Count > 1).Select(entry => entry.Name).ToList();
        Assert.IsTrue(multiCellNames.Count > 0, "Sanity check: the manifest must hold at least one multi-cell entry for this to prove anything.");

        foreach (var name in multiCellNames)
        {
            Assert.IsTrue(SpriteManifest.TryGetRandom(name, new MathUtility(new Random(4242)), out var first));

            for (var attempt = 0; attempt < 20; attempt++)
            {
                Assert.IsTrue(SpriteManifest.TryGetRandom(name, new MathUtility(new Random(4242)), out var again));
                Assert.AreEqual(first.SheetPath, again.SheetPath, $"TryGetRandom(\"{name}\") varied across identically-seeded randomizers.");
                Assert.AreEqual(first.SourceRectangle, again.SourceRectangle, $"TryGetRandom(\"{name}\") varied across identically-seeded randomizers.");
            }
        }
    }

    /// <summary>The other half of the seeding contract -- a different seed must actually be able to land on a different cell, or "seeded" would just mean "hardcoded". Asserted across the whole multi-cell name set rather than per name, since any single entry can legitimately repeat a cell across two seeds.</summary>
    [TestMethod]
    public void TryGetRandom_DifferentSeeds_CanResolveToDifferentCells()
    {
        var multiCellNames = LoadRawEntries().Where(entry => entry.Cells.Count > 1).Select(entry => entry.Name).ToList();
        Assert.IsTrue(multiCellNames.Count > 0, "Sanity check: the manifest must hold at least one multi-cell entry for this to prove anything.");

        var anyDiffered = false;
        foreach (var name in multiCellNames)
        {
            var seen = new HashSet<Rectangle>();
            for (var seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(SpriteManifest.TryGetRandom(name, new MathUtility(new Random(seed)), out var sprite));
                seen.Add(sprite.SourceRectangle);
            }

            anyDiffered |= seen.Count > 1;
        }

        Assert.IsTrue(anyDiffered, "Across 50 seeds, no multi-cell entry ever resolved to more than one cell -- the seed isn't reaching the roll.");
    }

    [TestMethod]
    public void TryGetRandom_UnknownName_ReturnsFalse()
    {
        var found = SpriteManifest.TryGetRandom("NoSuchName", new MathUtility(new Random(1)), out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void TryGetFirst_UnknownName_ReturnsFalse()
    {
        var found = SpriteManifest.TryGetFirst("NoSuchName", out _);

        Assert.IsFalse(found);
    }
}
