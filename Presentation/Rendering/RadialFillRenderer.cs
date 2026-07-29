using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

public sealed class RadialFillRenderer(GlyphRenderer glyphRenderer)
{
    private const int SliverCount = 72;
    private static readonly Color MaskColor = new Color(64, 64, 64) * 0.5f;

    public string Glyph { get; set; } = string.Empty;
    public Color GlyphColor { get; set; } = Color.White;
    public Color BackgroundColor { get; set; }
    public float FillPercentage { get; set; }

    public void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, SpriteFontBase font, Rectangle bounds)
    {
        spriteBatch.Draw(unitRectangle, bounds, BackgroundColor);
        glyphRenderer.DrawCentered(spriteBatch, font, Glyph, new Vector2(bounds.X, bounds.Y), new Vector2(bounds.Width, bounds.Height), GlyphColor);
        DrawRadialMask(spriteBatch, unitRectangle, bounds);
    }

    private void DrawRadialMask(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds)
    {
        var clampedFill = Math.Clamp(FillPercentage, 0f, 1f);
        if (clampedFill <= 0f)
        {
            return;
        }

        var center = new Vector2(bounds.Center.X, bounds.Center.Y);
        var radius = MathF.Min(bounds.Width, bounds.Height) / 2f;
        var sweptSlivers = (int)MathF.Ceiling(SliverCount * clampedFill);
        var sliverThickness = MathF.Max(1f, MathF.Tau * radius / SliverCount * 1.5f);
        var sliverSize = new Vector2(radius, sliverThickness);

        var angleStep = MathHelper.TwoPi / SliverCount;
        var angle = -MathHelper.PiOver2;

        for (var i = 0; i < sweptSlivers; i++)
        {
            spriteBatch.Draw(
                unitRectangle,
                center,
                null,
                MaskColor,
                angle,
                new Vector2(0f, 0.5f),
                sliverSize,
                SpriteEffects.None,
                0f);

            angle -= angleStep;
        }
    }
}
