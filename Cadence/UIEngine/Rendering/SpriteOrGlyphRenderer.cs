using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>
/// The sprite-vs-glyph decision, shared by every per-icon draw site: map entities, folder icons,
/// item cells. sprite is already resolved -- callers decide
/// how (the map view for map entities, SpriteSheetService.Sprites.TryGetFirst by name for HUD/item icons)
/// -- so this stays ignorant of where sprite data comes from, which is the only real difference
/// between call sites. Which tint/glyphColor to pass (e.g. gray for dead/disabled) is each
/// caller's own decision too -- kept out of here so this stays a pure draw primitive. The
/// sprite's own turn/flip and tint (from its manifest cell) always apply, the tint multiplied
/// into the caller's.
/// </summary>
public static class SpriteOrGlyphRenderer
{
    /// <summary>Draws sprite if present, else glyph if non-empty. Returns whether anything was actually drawn. outline defaults false -- a map passes true, since its glyphs sit over arbitrary terrain colours; an icon on a fixed window background doesn't need it.</summary>
    public static bool Draw(
        SpriteBatch spriteBatch,
        SpriteSheetService spriteSheetService,
        LabelRenderer labelRenderer,
        SpriteView? sprite,
        SpriteFontBase glyphFont,
        string glyph,
        Color glyphColor,
        Vector2 topLeft,
        Vector2 size,
        Color spriteTint,
        float alphaMultiplier = 1f,
        bool outline = false)
    {
        if (sprite is { } spriteView)
        {
            var texture = spriteSheetService.GetTexture(spriteView.SheetPath);
            var tint = spriteView.Tint is { } cellTint ? Multiply(spriteTint, cellTint) : spriteTint;
            SpriteRenderer.Draw(spriteBatch, texture, spriteView.SourceRectangle, topLeft, size, tint * alphaMultiplier, spriteView.Transform);
            return true;
        }

        if (!string.IsNullOrEmpty(glyph))
        {
            labelRenderer.DrawCentered(spriteBatch, glyphFont, glyph, topLeft, size, glyphColor, outline, alphaMultiplier);
            return true;
        }

        return false;
    }

    /// <summary>Each channel of first multiplied by the same channel of second, as SpriteBatch does a sprite's pixels by its colour.</summary>
    private static Color Multiply(Color first, Color second) =>
        new((byte)(first.R * second.R / 255), (byte)(first.G * second.G / 255), (byte)(first.B * second.B / 255), (byte)(first.A * second.A / 255));
}
