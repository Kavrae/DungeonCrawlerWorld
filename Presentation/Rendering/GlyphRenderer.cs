using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws a single glyph string at a position, and computes where to draw it so it's centered
/// within an arbitrary pixel footprint. Decoupled from Map/World/ComponentManager -- callers
/// (e.g. MapWindow) resolve which entity/font/position/color to use and pass those in as
/// plain values.
/// </summary>
public sealed class GlyphRenderer
{
    // Keyed by (font, glyph) rather than measured fresh every call -- TextBounds is a pure
    // function of the font and exact string, and the same (font, glyph) pairs repeat every
    // frame for every entity of a given race/terrain type across the visible map (thousands
    // of draws/frame at typical zoom), so this turns a per-draw font measurement into a
    // one-time cost per distinct glyph the game ever actually uses.
    private readonly Dictionary<(SpriteFontBase Font, string Glyph), Vector2> _inkCenterCache = [];

    public void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 position, Color color)
    {
        spriteBatch.DrawString(font, glyph, position, color);
    }

    /// <summary>
    /// Where glyph must be drawn so its actual rendered ink -- not the font's generic line
    /// box, which MeasureString returns and sits differently anchored than most glyphs' real
    /// ink (this is what previously made glyphs render too low: MeasureString("g") is a
    /// ~29px-tall line box, but the visible ink only occupies roughly its bottom two-thirds)
    /// -- centers within a footprintSize box whose top-left corner is footprintTopLeft. E.g. a
    /// 3x3 entity's footprint is 3 tiles wide/tall, not 1, so a "huge" font glyph centers
    /// across all three rather than sitting in the corner tile.
    /// </summary>
    public Vector2 GetCenteredPosition(SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var footprintCenter = footprintTopLeft + footprintSize / 2f;
        return footprintCenter - GetInkCenterAtOrigin(font, glyph);
    }

    public void DrawCentered(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize, Color color) =>
        Draw(spriteBatch, font, glyph, GetCenteredPosition(font, glyph, footprintTopLeft, footprintSize), color);

    /// <summary>The center of glyph's tight ink bounding box if drawn at (0,0) -- TextBounds translates linearly with position, so this alone is enough to center at any footprint.</summary>
    private Vector2 GetInkCenterAtOrigin(SpriteFontBase font, string glyph)
    {
        var key = (font, glyph);
        if (_inkCenterCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bounds = font.TextBounds(glyph, Vector2.Zero, null, 0f, 0f);
        var inkCenter = new Vector2((bounds.X + bounds.X2) / 2f, (bounds.Y + bounds.Y2) / 2f);
        _inkCenterCache[key] = inkCenter;
        return inkCenter;
    }
}