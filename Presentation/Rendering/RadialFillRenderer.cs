using FontStashSharp;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// spriteSheetService/spriteRenderer are optional -- every existing caller (ActionLockContent,
/// and HotbarContent's ability slots) draws glyphs only and never sets Sprite, so they're left
/// null and Draw falls back to the original glyph-only path unchanged. HotbarContent's item
/// slots are the one caller that supplies both (items can have real sprites, unlike abilities
/// today) and sets Sprite/SpriteTint per draw.
/// </summary>
public sealed class RadialFillRenderer(GlyphRenderer glyphRenderer, SpriteSheetService? spriteSheetService = null, SpriteRenderer? spriteRenderer = null)
{
    private const int SliverCount = 72;
    private static readonly Color MaskColor = new Color(64, 64, 64) * 0.5f;

    public string Glyph { get; set; } = string.Empty;
    public Color GlyphColor { get; set; } = Color.White;
    public Color BackgroundColor { get; set; }
    public float FillPercentage { get; set; }

    /// <summary>Drawn instead of Glyph when set (see SpriteOrGlyphRenderer's own sprite-first-glyph-fallback convention) -- reset to null between draws by callers that don't always have one (see HotbarContent.DrawAbilitySlot).</summary>
    public SpriteComponent? Sprite { get; set; }

    /// <summary>Tint applied to Sprite only -- GlyphColor plays the equivalent role for Glyph, kept separate since SpriteOrGlyphRenderer.Draw itself takes them as two independent parameters.</summary>
    public Color SpriteTint { get; set; } = Color.White;

    /// <summary>alphaMultiplier fades BackgroundColor and the sprite/glyph together -- HotbarContent's disabled-slot treatment (see its own doc comment) -- but never the radial mask itself, which stays scoped to whether this icon is on cooldown/unaffordable, not whether the whole slot is currently faded.</summary>
    public void Draw(SpriteBatch spriteBatch, Texture2D unitRectangle, SpriteFontBase font, Rectangle bounds, float alphaMultiplier = 1f)
    {
        spriteBatch.Draw(unitRectangle, bounds, BackgroundColor * alphaMultiplier);

        var position = new Vector2(bounds.X, bounds.Y);
        var size = new Vector2(bounds.Width, bounds.Height);

        if (Sprite is { } sprite && spriteSheetService is not null && spriteRenderer is not null)
        {
            SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, glyphRenderer, sprite, font, Glyph, GlyphColor, position, size, SpriteTint, alphaMultiplier);
        }
        else
        {
            glyphRenderer.DrawCentered(spriteBatch, font, Glyph, position, size, GlyphColor * alphaMultiplier);
        }

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
