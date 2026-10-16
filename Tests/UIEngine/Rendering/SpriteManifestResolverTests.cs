using SpriteFormat;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

[TestClass]
public sealed class SpriteManifestResolverTests
{
    private static SpriteManifestResolver ResolverOf(params SpriteManifestEntry[] entries) =>
        new(SpriteManifest.Parse(SpriteManifest.Serialize(entries)));

    /// <summary>Every name Presentation itself draws an icon or badge by; one missing would quietly draw its fallback glyph.</summary>
    [TestMethod]
    [DataRow("LootBag-Red")]
    [DataRow("Inventory")]
    [DataRow("AbilityScore")]
    [DataRow("AchievementCenter")]
    [DataRow("Currency-Gold")]
    [DataRow("Currency-Credit")]
    public void TryGetFirst_EveryNamePresentationDrawsBy_IsInTheShippedManifest(string name)
    {
        Assert.IsTrue(TestSprites.Resolver.TryGetFirst(name, out _), $"'{name}' is not in Content/SpriteManifest.json.");
    }

    /// <summary>Every shipped entry's icon is its first cell, placed, turned and tinted as the entry says.</summary>
    [TestMethod]
    public void TryGetFirst_EveryShippedEntry_IsItsFirstCell()
    {
        var manifest = SpriteManifest.Load(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json"));

        foreach (var entry in manifest.Entries)
        {
            var found = TestSprites.Resolver.TryGetFirst(entry.Name, out var sprite);

            Assert.AreEqual(entry.Cells[0] is not null, found, $"'{entry.Name}'");
            if (entry.Cells[0] is { } first)
            {
                Assert.AreEqual(SpriteManifestResolver.ToSpriteView(first, entry.PlacementFor(first)), sprite, $"'{entry.Name}'");
            }
        }
    }

    /// <summary>The entity variant hash moved from Game unchanged, salt included, so no entity changed its look: pinned against the formula it had.</summary>
    [TestMethod]
    [DataRow(0u)]
    [DataRow(1u)]
    [DataRow(12345u)]
    [DataRow(0xDEADBEEFu)]
    public void TryGetForEntity_PicksTheCellTheOriginalHashPicked(uint seed)
    {
        var cells = Enumerable.Range(0, 5).Select(index => new SpriteManifestCell("sheet.png", index, 0, 16, 16)).ToList();
        var resolver = ResolverOf(new SpriteManifestEntry("Five", [.. cells]));

        var mixed = (seed ^ 0x85EBCA6Bu) * 0x9E3779B97F4A7C15UL;
        mixed ^= mixed >> 31;
        var expectedIndex = (int)(mixed % 5u);

        Assert.IsTrue(resolver.TryGetForEntity("Five", seed, out var sprite));
        Assert.AreEqual(expectedIndex * 16, sprite.SourceRectangle.X);
    }

    /// <summary>SpriteSheetService caches textures by reference: two entries naming one sheet must share one path instance, or each loads its own copy.</summary>
    [TestMethod]
    public void TwoEntriesNamingOneSheet_ShareOneSheetPathInstance()
    {
        var resolver = ResolverOf(
            new SpriteManifestEntry("First", [new SpriteManifestCell("shared-sheet.png", 0, 0, 16, 16)]),
            new SpriteManifestEntry("Second", [new SpriteManifestCell("shared-sheet.png", 1, 0, 16, 16)]));

        Assert.IsTrue(resolver.TryGetFirst("First", out var first));
        Assert.IsTrue(resolver.TryGetFirst("Second", out var second));
        Assert.IsTrue(ReferenceEquals(first.SheetPath, second.SheetPath));
    }

    [TestMethod]
    public void TryGetFirst_AnAbsentName_IsAMiss()
    {
        Assert.IsFalse(TestSprites.Resolver.TryGetFirst("No such sprite", out _));
    }

    [TestMethod]
    public void TryGetFirst_AnUnfinishedSetWhoseFirstSlotIsEmpty_IsAMiss()
    {
        var cells = Enumerable.Range(0, 16).Select(index => (SpriteManifestCell?)new SpriteManifestCell("sheet.png", index, 0, 16, 16)).ToList();
        cells[0] = null;

        var resolver = ResolverOf(new SpriteManifestEntry("Unfinished", cells, Linking: SpriteLinking.Cardinal));

        Assert.IsFalse(resolver.TryGetFirst("Unfinished", out _));
    }

    [TestMethod]
    public void TryGetFirst_APlacedTurnedTintedCell_CarriesAllThree()
    {
        var cell = new SpriteManifestCell("sheet.png", 1, 2, 16, 16, Rotation: 90, FlipHorizontal: true, Tint: "#80C0FF");

        var resolver = ResolverOf(new SpriteManifestEntry("Mossy", [cell], PixelsPerTile: 8));

        Assert.IsTrue(resolver.TryGetFirst("Mossy", out var sprite));
        Assert.AreEqual(new Microsoft.Xna.Framework.Rectangle(16, 32, 16, 16), sprite.SourceRectangle);
        Assert.AreEqual(new SpritePlacement(8, 8, 16), sprite.Placement);
        Assert.AreEqual(new SpriteCellTransform(90, true, false), sprite.Transform);
        Assert.AreEqual(new Microsoft.Xna.Framework.Color(128, 192, 255), sprite.Tint);
    }
}
