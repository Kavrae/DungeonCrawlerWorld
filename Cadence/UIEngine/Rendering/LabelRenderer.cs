using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>
/// Draws a single glyph string at a position, and computes where to draw it so it's centered
/// within an arbitrary pixel footprint. Decoupled from any world or store -- callers resolve
/// which font, position and colour to use and pass those in as plain values.
/// </summary>
public sealed class LabelRenderer
{
    // Keyed by (font, glyph) rather than measured fresh every call -- TextBounds is a pure
    // function of the font and exact string, and the same (font, glyph) pairs repeat every
    // frame across the visible map, so this turns a per-draw font measurement into a
    // one-time cost per distinct glyph.
    private readonly Dictionary<(SpriteFontBase Font, string Glyph), Vector2> _inkCenterCache = [];

    /// <summary>Draws glyph at position, outlined or not.</summary>
    /// <remarks>
    /// outline defaults false -- plain UI text sits on its own window background and doesn't need it. Map-drawn glyphs
    /// pass true so they stay legible against whatever terrain is underneath.
    ///
    /// position is rounded to the nearest whole pixel: every render pass uses SamplerState.PointClamp (see
    /// ElementPoolService.ResetRenderState), and point sampling a font atlas at a fractional position crops or shifts a
    /// glyph by about a texel -- which the `/ 2f` centring of GetLeftAlignedPosition/GetRightAlignedPosition produces
    /// whenever the footprint's height minus LineHeight is odd. TextWindow.DrawContent, the one direct DrawString in the UI
    /// layer, rounds the same way.
    /// </remarks>
    /// <param name="alphaMultiplier">Fades the text, its outline included -- pass it here rather than pre-multiplied into color, which would fade the fill and leave the outline solid.</param>
    public void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 position, Color color, bool outline = false, float alphaMultiplier = 1f)
    {
        if (outline)
        {
            ContrastTextRenderer.Draw(spriteBatch, font, glyph, position, alphaMultiplier, color);
            return;
        }

        spriteBatch.DrawString(font, glyph, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color * alphaMultiplier);
    }

    /// <summary>
    /// Where glyph must be drawn so its actual rendered ink -- not the font's line box, which MeasureString returns and
    /// which sits differently from most glyphs' ink -- centers within a footprintSize box whose top-left corner is
    /// footprintTopLeft. A 3x3 entity's glyph centers across all three tiles.
    /// </summary>
    public Vector2 GetCenteredPosition(SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var footprintCenter = footprintTopLeft + footprintSize / 2f;
        return footprintCenter - GetInkCenterAtOrigin(font, glyph);
    }

    /// <param name="alphaMultiplier">See Draw.</param>
    public void DrawCentered(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize, Color color, bool outline = false, float alphaMultiplier = 1f) =>
        Draw(spriteBatch, font, glyph, GetCenteredPosition(font, glyph, footprintTopLeft, footprintSize), color, outline, alphaMultiplier);

    /// <summary>
    /// Where text must be drawn so it's flush against footprintSize's right edge and vertically centered within it.
    /// </summary>
    /// <remarks>
    /// Horizontally, the line box sits flush right, not each string's ink, so a column of right-aligned rows lines up.
    /// Vertically, it centers against font.LineHeight rather than MeasureString(text).Y, whose box sits below where the
    /// ink renders and would push descenders past the footprint's bottom edge. LineHeight is per font, not per string, so
    /// a column of rows stays consistent whichever have descenders.
    /// </remarks>
    /// <param name="textWidth">The text's measured width, kept by the caller (a TextSizeCache, or measured when the text was set) rather than measured every draw.</param>
    public Vector2 GetRightAlignedPosition(SpriteFontBase font, float textWidth, Vector2 footprintTopLeft, Vector2 footprintSize) =>
        footprintTopLeft + new Vector2(footprintSize.X - textWidth, (footprintSize.Y - font.LineHeight) / 2f);

    /// <param name="textWidth">text's measured width; see GetRightAlignedPosition.</param>
    public void DrawRightAligned(SpriteBatch spriteBatch, SpriteFontBase font, string text, float textWidth, Vector2 footprintTopLeft, Vector2 footprintSize, Color color) =>
        Draw(spriteBatch, font, text, GetRightAlignedPosition(font, textWidth, footprintTopLeft, footprintSize), color);

    /// <summary>Where text must be drawn so it's flush against footprintSize's left edge and vertically centered within it -- the left-aligned counterpart to GetRightAlignedPosition (it centres against LineHeight the same way), for a row that needs both (e.g. a context-menu option's label on the left, its hotkey on the right).</summary>
    public Vector2 GetLeftAlignedPosition(SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize) =>
        footprintTopLeft + new Vector2(0, (footprintSize.Y - font.LineHeight) / 2f);

    public void DrawLeftAligned(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize, Color color) =>
        Draw(spriteBatch, font, text, GetLeftAlignedPosition(font, footprintTopLeft, footprintSize), color);

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