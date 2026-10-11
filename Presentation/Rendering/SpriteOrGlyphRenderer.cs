using FontStashSharp;
using Game.Modules.Core.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Sprites;

namespace Presentation.Rendering;

/// <summary>
/// The sprite-vs-glyph decision, shared by every per-icon draw site: MapWindow (map entities),
/// Folder (HUD icons), and inventory item cells. sprite is already resolved -- callers decide
/// how (the map view for map entities, SpriteViews.TryGetFirst by name for HUD/item icons)
/// -- so this stays ignorant of where sprite data comes from, which is the only real difference
/// between call sites. Which tint/glyphColor to pass (e.g. gray for dead/disabled) is each
/// caller's own decision too -- kept out of here so this stays a pure draw primitive. The
/// sprite's own turn/flip and tint (from its manifest cell) always apply, the tint multiplied
/// into the caller's.
/// </summary>
public static class SpriteOrGlyphRenderer
{
    /// <summary>Draws sprite if present, else glyph if non-empty. Returns whether anything was actually drawn. outline defaults false -- only MapWindow's own entity/terrain draws pass true, since those glyphs sit over arbitrary terrain colors; Folder/inventory item-cell icons sit on a fixed window background and don't need it.</summary>
    public static bool Draw(
        SpriteBatch spriteBatch,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer,
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
            var tint = spriteView.Tint is { } cellTint ? SpriteTint.Multiply(spriteTint, cellTint) : spriteTint;
            spriteRenderer.Draw(spriteBatch, texture, spriteView.SourceRectangle, topLeft, size, tint * alphaMultiplier, spriteView.Transform);
            return true;
        }

        if (!string.IsNullOrEmpty(glyph))
        {
            labelRenderer.DrawCentered(spriteBatch, glyphFont, glyph, topLeft, size, glyphColor * alphaMultiplier, outline);
            return true;
        }

        return false;
    }
}
