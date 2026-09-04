using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Tests.Presentation.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class LabelRendererTests
{
    /// <summary>
    /// Regression: centering used to be computed against MeasureString's generic line-height
    /// box, which sits anchored well above where most glyphs' actual ink renders (e.g. "g" at
    /// font size 24 measures a ~29px-tall box, but its ink only occupies roughly Y=[10,29]
    /// within that box) -- centering by the box therefore drew every glyph too low. The real
    /// invariant is that the glyph's actual rendered ink (TextBounds), not the box, centers on
    /// the footprint.
    /// </summary>
    [TestMethod]
    public void GetCenteredPosition_ResultingGlyphInkCenterMatchesFootprintCenter()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        const string glyph = "g";
        var footprintTopLeft = new Vector2(30, 60);
        var footprintSize = new Vector2(72, 72); // e.g. a 3x3 Huge footprint at 24px tiles.

        var position = renderer.GetCenteredPosition(font, glyph, footprintTopLeft, footprintSize);

        var inkBoundsAtZero = font.TextBounds(glyph, Vector2.Zero, null, 0f, 0f);
        var inkCenter = position + new Vector2((inkBoundsAtZero.X + inkBoundsAtZero.X2) / 2f, (inkBoundsAtZero.Y + inkBoundsAtZero.Y2) / 2f);
        var footprintCenter = footprintTopLeft + footprintSize / 2f;

        Assert.AreEqual(footprintCenter.X, inkCenter.X, 0.01f);
        Assert.AreEqual(footprintCenter.Y, inkCenter.Y, 0.01f);
    }

    /// <summary>
    /// Regression: before centering accounted for an entity's actual footprint size, a Large/
    /// Huge entity's glyph was drawn at the same fixed offset as a Medium (1x1) one, landing
    /// near the corner of its footprint instead of the middle -- the position must move
    /// further from the footprint's origin as the footprint grows, not stay fixed.
    /// </summary>
    [TestMethod]
    public void GetCenteredPosition_LargerFootprint_MovesPositionFurtherFromOrigin()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(36);
        const string glyph = "g";
        var origin = Vector2.Zero;

        var oneTilePosition = renderer.GetCenteredPosition(font, glyph, origin, new Vector2(12, 12));
        var threeTilePosition = renderer.GetCenteredPosition(font, glyph, origin, new Vector2(36, 36));

        Assert.IsGreaterThan(oneTilePosition.X, threeTilePosition.X);
        Assert.IsGreaterThan(oneTilePosition.Y, threeTilePosition.Y);
    }

    [TestMethod]
    public void GetCenteredPosition_FootprintTopLeftOffset_TranslatesResultByTheSameAmount()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(8);
        const string glyph = "f";
        var footprintSize = new Vector2(12, 12);

        var atOrigin = renderer.GetCenteredPosition(font, glyph, Vector2.Zero, footprintSize);
        var shifted = renderer.GetCenteredPosition(font, glyph, new Vector2(100, 200), footprintSize);

        Assert.AreEqual(atOrigin.X + 100, shifted.X, 0.01f);
        Assert.AreEqual(atOrigin.Y + 200, shifted.Y, 0.01f);
    }

    /// <summary>
    /// Regression coverage for the ink-center cache being keyed by (font, glyph), not font
    /// alone -- "g" and "," have very different ink shapes/positions at the same font size,
    /// so a key collision would make their centered positions come back identical.
    /// </summary>
    [TestMethod]
    public void GetCenteredPosition_DifferentGlyphsSameFont_AreNotConflated()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = Vector2.Zero;
        var footprintSize = new Vector2(24, 24);

        var positionG = renderer.GetCenteredPosition(font, "g", footprintTopLeft, footprintSize);
        var positionComma = renderer.GetCenteredPosition(font, ",", footprintTopLeft, footprintSize);

        Assert.AreNotEqual(positionG, positionComma);
    }

    /// <summary>Caching must not make results stale or order-dependent -- repeat and interleaved lookups return the same value every time.</summary>
    [TestMethod]
    public void GetCenteredPosition_RepeatedAndInterleavedLookups_ReturnConsistentResults()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = new Vector2(5, 5);
        var footprintSize = new Vector2(24, 24);

        var first = renderer.GetCenteredPosition(font, "g", footprintTopLeft, footprintSize);
        renderer.GetCenteredPosition(font, "A", footprintTopLeft, footprintSize); // Interleave a different glyph.
        var second = renderer.GetCenteredPosition(font, "g", footprintTopLeft, footprintSize);

        Assert.AreEqual(first, second);
    }

    /// <summary>
    /// Regression: GetLeftAlignedPosition/GetRightAlignedPosition used to center vertically against
    /// MeasureString(text).Y -- the same "generic line box" GetCenteredPosition's own doc comment
    /// describes (e.g. "g" at font size 24 measures a ~29px-tall box, but its ink only occupies
    /// roughly the box's bottom two-thirds). Centering a tight-height footprint against that
    /// oversized box pushed text low enough to bleed a descender past the footprint's own bottom
    /// edge (confirmed live: AbilityScoreModifierRow, ShopItemStackCell, Tooltip's status line --
    /// every DrawLeftAligned/DrawRightAligned consumer). The fix centers against font.LineHeight
    /// instead -- a footprint exactly LineHeight tall should therefore get zero vertical offset.
    /// </summary>
    [TestMethod]
    public void GetLeftAlignedPosition_FootprintExactlyLineHeightTall_NoVerticalOffset()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = new Vector2(10, 20);
        var footprintSize = new Vector2(200, font.LineHeight);

        var position = renderer.GetLeftAlignedPosition(font, "Understocked", footprintTopLeft, footprintSize);

        Assert.AreEqual(footprintTopLeft.Y, position.Y, 0.01f);
    }

    [TestMethod]
    public void GetRightAlignedPosition_FootprintExactlyLineHeightTall_NoVerticalOffset()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = new Vector2(10, 20);
        var footprintSize = new Vector2(200, font.LineHeight);

        var position = renderer.GetRightAlignedPosition(font, "Some Source : +2", footprintTopLeft, footprintSize);

        Assert.AreEqual(footprintTopLeft.Y, position.Y, 0.01f);
    }

    /// <summary>Right-aligned text must still flush against the footprint's own right edge -- the LineHeight fix only changes the Y formula, not X.</summary>
    [TestMethod]
    public void GetRightAlignedPosition_FlushesTextToTheFootprintsRightEdge()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = Vector2.Zero;
        var footprintSize = new Vector2(200, font.LineHeight);
        const string text = "Some Source : +2";

        var position = renderer.GetRightAlignedPosition(font, text, footprintTopLeft, footprintSize);

        Assert.AreEqual(footprintSize.X - font.MeasureString(text).X, position.X, 0.01f);
    }

    /// <summary>Different strings in a row of right-aligned text (e.g. one with descenders, one without) must land on the same Y -- the whole point of centering against the font's own LineHeight rather than each string's individual MeasureString box.</summary>
    [TestMethod]
    public void GetRightAlignedPosition_DifferentStringsSameFootprint_SameVerticalPosition()
    {
        var renderer = new LabelRenderer();
        var font = TestFonts.Shared.GetFont(24);
        var footprintTopLeft = new Vector2(10, 20);
        var footprintSize = new Vector2(200, 40);

        var withDescender = renderer.GetRightAlignedPosition(font, "Source : +2", footprintTopLeft, footprintSize);
        var withoutDescender = renderer.GetRightAlignedPosition(font, "AC : 5", footprintTopLeft, footprintSize);

        Assert.AreEqual(withDescender.Y, withoutDescender.Y, 0.01f);
    }
}