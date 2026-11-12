using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using System.Runtime.CompilerServices;
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

    /// <summary>How many strings' ink centres are kept before all are forgotten: text that keeps changing (a count, a price) would otherwise be kept for the session.</summary>
    internal const int MaximumKeptInkCenters = 2048;

    private const int RecentInkCenterSlots = 256;

    // TextBounds is a pure function of the font and the exact string, and the same glyphs repeat every frame across
    // the visible map, so each is measured once.
    private readonly Dictionary<string, Vector2> _inkCentersAtOrigin = [];

    // The same string instance is asked for every frame it is drawn, so it is found here by reference before its
    // characters are hashed for the dictionary.
    private readonly (string? Text, Vector2 InkCenter)[] _recentInkCenters = new (string? Text, Vector2 InkCenter)[RecentInkCenterSlots];

    /// <summary>How many strings' ink centres are kept now.</summary>
    internal int KeptInkCenterCount => _inkCentersAtOrigin.Count;

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
        ref var recent = ref _recentInkCenters[RuntimeHelpers.GetHashCode(glyph) & (RecentInkCenterSlots - 1)];
        if (ReferenceEquals(recent.Text, glyph))
        {
            return recent.InkCenter;
        }

        if (!_inkCentersAtOrigin.TryGetValue(glyph, out var inkCenter))
        {
            if (_inkCentersAtOrigin.Count >= MaximumKeptInkCenters)
            {
                _inkCentersAtOrigin.Clear();
                Array.Clear(_recentInkCenters);
            }

            var bounds = Font.TextBounds(glyph, Vector2.Zero, null, 0f, 0f);
            inkCenter = new Vector2((bounds.X + bounds.X2) / 2f, (bounds.Y + bounds.Y2) / 2f);
            _inkCentersAtOrigin[glyph] = inkCenter;
        }

        recent = (glyph, inkCenter);
        return inkCenter;
    }
}
