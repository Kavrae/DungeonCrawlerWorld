using FontStashSharp;
using Microsoft.Xna.Framework;

namespace UIEngine.Rendering;

/// <summary>
/// Generic high-contrast text draw primitive -- a full 1px outline (all eight neighbours, not
/// just a single drop-shadow corner) behind a fill, legible against any background. Same
/// visual language as BorderStyle.FlatContrast (light content, black surround). For countdowns,
/// stack counts and key labels drawn over icons, and for LabelRenderer's outlined glyphs, so text
/// stays legible over whatever's rendered beneath it.
/// </summary>
public static class ContrastTextRenderer
{
    public static readonly Color FillColor = Color.White;
    public static readonly Color OutlineColor = Color.Black;

    /// <summary>position is the text's own top-left -- callers differ only in where they place it (centred above, within or below an icon). alphaMultiplier defaults to fully opaque -- a lower value fades the outline and the fill together, as a disabled icon's text does. fillColor defaults to FillColor (white) -- LabelRenderer passes its caller-supplied glyph color instead, since glyph fill color is meaningful (race/terrain tinting) and only the outline needs to stay fixed black.</summary>
    /// <remarks>
    /// position is rounded to the nearest whole pixel, as LabelRenderer.Draw rounds it: a glyph drawn at a fractional position
    /// under PointClamp is cropped by about a texel. The target draws the outline (ISpriteDrawTarget.DrawOutlinedString):
    /// SpriteEffectBatch as two quads a glyph, the outline layer then the fill, outlined by the sprite effect.
    /// </remarks>
    public static void Draw<TTarget>(TTarget target, SpriteFontBase font, string text, Vector2 position, float alphaMultiplier = 1f, Color? fillColor = null, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var roundedPosition = new Vector2(MathF.Round(position.X), MathF.Round(position.Y));
        target.DrawOutlinedString(font, text, roundedPosition, fillColor ?? FillColor, alphaMultiplier, style);
    }
}
