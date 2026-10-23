using SpriteFormat;

namespace Tests.SpriteManifestFormat;

[TestClass]
public sealed class SpriteEntryRulesTests
{
    private static SpriteManifestCell Cell(int columnSpan = 0, int rowSpan = 0) => new("sheet.png", 0, 0, 16, 16, columnSpan, rowSpan);

    [TestMethod]
    public void SpanOf_TheSavedSpan_WhenThereIsOne() =>
        Assert.AreEqual((2, 3), SpriteEntryRules.SpanOf([Cell()], savedColumnSpan: 2, savedRowSpan: 3));

    [TestMethod]
    public void SpanOf_WithNoneSaved_IsTheMostCommonCellSpan() =>
        Assert.AreEqual((2, 1), SpriteEntryRules.SpanOf([Cell(2), Cell(2), Cell(), null], 0, 0));

    [TestMethod]
    public void SpanOf_NoCells_IsOneByOne() =>
        Assert.AreEqual((1, 1), SpriteEntryRules.SpanOf([null], 0, 0));

    [TestMethod]
    public void CountMisfits_CountsCellsOfAnotherSpan_NotEmptySlots() =>
        Assert.AreEqual(1, SpriteEntryRules.CountMisfits([Cell(), Cell(2), null], 1, 1));

    [TestMethod]
    public void HasMisfit_LooksAtAWallsTopToo()
    {
        var entry = new SpriteManifestEntry("Wall", [Cell()], Top: new SpriteManifestPart([Cell(), Cell(1, 2)], ColumnSpan: 1, RowSpan: 1));

        Assert.IsTrue(SpriteEntryRules.HasMisfit(entry));
        Assert.IsFalse(SpriteEntryRules.HasMisfit(entry with { Top = null }));
    }
}

[TestClass]
public sealed class SpriteLinkLayoutTests
{
    private static readonly string[] Plus =
    [
        ".#.",
        "###",
        ".#.",
    ];

    private static bool IsWall(int x, int y) => y is >= 0 and < 3 && x is >= 0 and < 3 && Plus[y][x] == '#';

    [TestMethod]
    public void Cardinal_TheMiddleOfAPlus_JoinsEverySide() =>
        Assert.AreEqual(SpriteLinks.North | SpriteLinks.East | SpriteLinks.South | SpriteLinks.West, SpriteLinkLayout.Cardinal.Compute(IsWall, 1, 1));

    [TestMethod]
    public void Cardinal_AnArmOfAPlus_JoinsOnlyTheMiddle() =>
        Assert.AreEqual(SpriteLinks.South, SpriteLinkLayout.Cardinal.Compute(IsWall, 1, 0));

    [TestMethod]
    public void Horizontal_JoinsOnlyLeftAndRight() =>
        Assert.AreEqual(SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest, SpriteLinkLayout.Horizontal.Compute(IsWall, 1, 1));

    [TestMethod]
    public void PieceFor_AnEmptySlot_IsTheFallbackPiece()
    {
        var slots = SpriteLinkLayout.Cardinal.NewVariantSet();
        var fallback = new SpriteManifestCell("sheet.png", 1, 1, 16, 16);
        slots[SpriteLinkLayout.Cardinal.FallbackMask] = fallback;

        Assert.AreSame(fallback, SpriteLinkLayout.Cardinal.PieceFor(slots, SpriteLinks.North));
    }
}
