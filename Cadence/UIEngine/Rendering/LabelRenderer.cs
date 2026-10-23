using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;

namespace UIEngine.Rendering;

/// <summary>
/// Draws a single glyph string at a position, and computes where to draw it so it's centered
/// within an arbitrary pixel footprint. Decoupled from any world or store -- callers resolve
/// which font, position and colour to use and pass those in as plain values.
/// </summary>
/// <remarks>Holds nothing: centring reads a glyph's ink centre from its font's FontStashTextMeasurer, which keeps it.</remarks>
public static class LabelRenderer
{
    /// <summary>Draws glyph at position, outlined or not.</summary>
    /// <remarks>
    /// outline defaults false -- plain UI text sits on its own window background and doesn't need it. Map-drawn glyphs
    /// pass true so they stay legible against whatever terrain is underneath.
    ///
    /// position is rounded to the nearest whole pixel: every render pass uses SamplerState.PointClamp (see
    /// UiRenderService.BeginFrame), and point sampling a font atlas at a fractional position crops or shifts a
    /// glyph by about a texel -- which the `/ 2f` centring of GetLeftAlignedPosition/GetRightAlignedPosition produces
    /// whenever the footprint's height minus LineHeight is odd. TextWindow.DrawContent, the one direct DrawString in the UI
    /// layer, rounds the same way.
    /// </remarks>
    /// <param name="alphaMultiplier">Fades the text, its outline included -- pass it here rather than pre-multiplied into color, which would fade the fill and leave the outline solid.</param>
    public static void Draw<TTarget>(TTarget target, SpriteFontBase font, string glyph, Vector2 position, Color color, bool outline = false, float alphaMultiplier = 1f, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (outline)
        {
            ContrastTextRenderer.Draw(target, font, glyph, position, alphaMultiplier, color, style);
            return;
        }

        target.DrawString(font, glyph, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color * alphaMultiplier, style);
    }

    /// <summary>
    /// Where glyph must be drawn in fontMeasurer's font so its actual rendered ink -- not the font's line box, which
    /// MeasureString returns and which sits differently from most glyphs' ink -- centers within a footprintSize box whose
    /// top-left corner is footprintTopLeft. A 3x3 entity's glyph centers across all three tiles.
    /// </summary>
    public static Vector2 GetCenteredPosition(FontStashTextMeasurer fontMeasurer, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var footprintCenter = footprintTopLeft + footprintSize / 2f;
        return footprintCenter - fontMeasurer.GetInkCenterAtOrigin(glyph);
    }

    /// <param name="fontMeasurer">The font to draw in, measured -- see GetCenteredPosition.</param>
    /// <param name="alphaMultiplier">See Draw.</param>
    public static void DrawCentered<TTarget>(TTarget target, FontStashTextMeasurer fontMeasurer, string glyph, Vector2 footprintTopLeft, Vector2 footprintSize, Color color, bool outline = false, float alphaMultiplier = 1f, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        Draw(target, fontMeasurer.Font, glyph, GetCenteredPosition(fontMeasurer, glyph, footprintTopLeft, footprintSize), color, outline, alphaMultiplier, style);

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
    public static Vector2 GetRightAlignedPosition(SpriteFontBase font, float textWidth, Vector2 footprintTopLeft, Vector2 footprintSize) =>
        footprintTopLeft + new Vector2(footprintSize.X - textWidth, (footprintSize.Y - font.LineHeight) / 2f);

    /// <param name="textWidth">text's measured width; see GetRightAlignedPosition.</param>
    public static void DrawRightAligned<TTarget>(TTarget target, SpriteFontBase font, string text, float textWidth, Vector2 footprintTopLeft, Vector2 footprintSize, Color color)
        where TTarget : ISpriteDrawTarget =>
        Draw(target, font, text, GetRightAlignedPosition(font, textWidth, footprintTopLeft, footprintSize), color);

    /// <summary>Where text must be drawn so it's flush against footprintSize's left edge and vertically centered within it -- the left-aligned counterpart to GetRightAlignedPosition (it centres against LineHeight the same way), for a row that needs both (e.g. a context-menu option's label on the left, its hotkey on the right).</summary>
    public static Vector2 GetLeftAlignedPosition(SpriteFontBase font, Vector2 footprintTopLeft, Vector2 footprintSize) =>
        footprintTopLeft + new Vector2(0, (footprintSize.Y - font.LineHeight) / 2f);

    public static void DrawLeftAligned<TTarget>(TTarget target, SpriteFontBase font, string text, Vector2 footprintTopLeft, Vector2 footprintSize, Color color)
        where TTarget : ISpriteDrawTarget =>
        Draw(target, font, text, GetLeftAlignedPosition(font, footprintTopLeft, footprintSize), color);
}
