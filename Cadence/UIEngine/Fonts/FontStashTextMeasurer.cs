using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using System.Text;

namespace UIEngine.Fonts;

/// <summary>Measures text in one FontStashSharp font: widths through Engine.Utilities' ITextMeasurer, so
/// StringUtility's word-wrap algorithms stay free of any rendering-library dependency, and each glyph's ink centre.</summary>
/// <remarks>
/// FontStashSharp measures a string or a StringBuilder but not a span, so a span is copied into one reused
/// StringBuilder and measured there -- no allocation per measurement. One instance per font, from
/// FontService.GetTextMeasurer; not thread-safe, like the font itself.
/// </remarks>
public sealed class FontStashTextMeasurer(SpriteFontBase font) : ITextMeasurer
{
    private readonly StringBuilder _measuredText = new();

    // TextBounds is a pure function of the font and the exact string, and the same glyphs repeat every frame across
    // the visible map, so each is measured once.
    private readonly Dictionary<string, Vector2> _inkCentersAtOrigin = [];

    /// <summary>The font this measures.</summary>
    public SpriteFontBase Font { get; } = font;

    public float MeasureWidth(ReadOnlySpan<char> text)
    {
        _measuredText.Clear().Append(text);
        return Font.MeasureString(_measuredText).X;
    }

    /// <summary>The centre of glyph's tight ink bounding box if drawn at (0,0) -- TextBounds translates linearly with position, so this alone is enough to centre it in any footprint.</summary>
    public Vector2 GetInkCenterAtOrigin(string glyph)
    {
        if (_inkCentersAtOrigin.TryGetValue(glyph, out var cached))
        {
            return cached;
        }

        var bounds = Font.TextBounds(glyph, Vector2.Zero, null, 0f, 0f);
        var inkCenter = new Vector2((bounds.X + bounds.X2) / 2f, (bounds.Y + bounds.Y2) / 2f);
        _inkCentersAtOrigin[glyph] = inkCenter;
        return inkCenter;
    }
}
