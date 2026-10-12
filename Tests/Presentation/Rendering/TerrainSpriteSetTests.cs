using Presentation.Rendering;
using SpriteFormat;

namespace Tests.Presentation.Rendering;

[TestClass]
public sealed class TerrainSpriteSetTests
{
    /// <summary>The checked-in wall entry the built-in stone wall draws with; expectations are read from it, so editing its art doesn't break them.</summary>
    private const string WallSpriteName = "wall-stone";

    private static SpriteManifestEntry ShippedEntry(string name)
    {
        var manifest = SpriteManifest.Load(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json"));
        Assert.IsTrue(manifest.TryGetEntry(name, out var entry), $"'{name}' is not in the shipped manifest.");
        return entry;
    }

    private static TerrainSpriteSet SetOf(string name)
    {
        Assert.IsTrue(TestSprites.Resolver.TryGetTerrainSprites(name, out var sprites));
        return sprites;
    }

    private static SpriteView Expected(SpriteManifestPart part, int index)
    {
        var cell = part.Cells[index]!;
        return SpriteManifestResolver.ToSpriteView(cell, part.PlacementFor(cell));
    }

    [TestMethod]
    public void AFloorWithNoTop_HasNone()
    {
        var sprites = SetOf("Dirt");

        Assert.IsFalse(sprites.HasTop);
        Assert.IsFalse(sprites.TryGetTop(0, mask: null, out _));
    }

    [TestMethod]
    public void AWallsTop_FollowsTheSeedsVariant_FallsBackToItsFullyJoinedPiece_RaisedByItsFacesHeight()
    {
        var entry = ShippedEntry(WallSpriteName);
        var top = entry.Top!;
        var sprites = SetOf(WallSpriteName);

        for (var variant = 0; variant < sprites.VariantCount; variant++)
        {
            var expectedIndex = SpriteLinks.CellIndexFor(top, variant, SpriteLinks.FallbackMask(top.Linking));

            Assert.IsTrue(sprites.TryGetTop((uint)variant, mask: null, out var sprite));
            Assert.AreEqual(Expected(top, expectedIndex), sprite);
        }

        Assert.AreEqual(entry.Face.HeightRows(), sprites.TopRaisedRows);
    }

    [TestMethod]
    public void AWallFace_IsItsPieceForTheMask_InTheSeedsSet()
    {
        var entry = ShippedEntry(WallSpriteName);
        var sprites = SetOf(WallSpriteName);

        for (var variant = 0; variant < sprites.VariantCount; variant++)
        {
            foreach (var mask in new[] { SpriteLinks.Standalone, SpriteLinks.HorizontalEast, SpriteLinks.HorizontalWest, SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest })
            {
                var expectedIndex = SpriteLinks.CellIndexFor(entry.Face, variant, mask);

                Assert.IsTrue(sprites.TryGetFace((uint)variant, mask, out var sprite));
                Assert.AreEqual(Expected(entry.Face, expectedIndex), sprite);
            }
        }
    }

    [TestMethod]
    public void VariantCount_IsAPercentileWithChances_OtherwiseTheLargerSetCount()
    {
        var entry = ShippedEntry(WallSpriteName);

        var expectedCount = entry.VariantWeights is not null || entry.Top?.VariantWeights is not null
            ? SpriteLinks.PercentileRange
            : System.Math.Max(entry.Cells.Count / SpriteLinks.CellsPerSet(entry.Linking), entry.Top!.Cells.Count / SpriteLinks.CellsPerSet(entry.Top.Linking));

        Assert.AreEqual(expectedCount, SetOf(WallSpriteName).VariantCount);
    }

    [TestMethod]
    public void AFloorsOption_IsTheOneTheSeedPicks_AndASeedPastTheCountWraps()
    {
        var entry = ShippedEntry("Grass");
        var sprites = SetOf("Grass");
        Assert.IsGreaterThan(1, sprites.VariantCount);

        Assert.IsTrue(sprites.TryGetFace(1, mask: null, out var picked));
        Assert.IsTrue(sprites.TryGetFace((uint)(1 + 7 * sprites.VariantCount), mask: null, out var wrapped));

        Assert.AreEqual(Expected(entry.Face, 1), picked);
        Assert.AreEqual(picked, wrapped);
    }
}
