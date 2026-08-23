using FontStashSharp;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// The sprite-vs-glyph decision, shared by every per-icon draw site: MapWindow (map entities),
/// Folder (HUD icons), and inventory item cells. sprite is already resolved -- callers decide
/// how (an ECS pool lookup for map entities, SpriteManifest.TryGet by name for HUD/item icons)
/// -- so this stays ignorant of where sprite data comes from, which is the only real difference
/// between call sites. Which tint/glyphColor to pass (e.g. gray for dead/disabled) is each
/// caller's own decision too -- kept out of here so this stays a pure draw primitive.
/// </summary>
public static class SpriteOrGlyphRenderer
{
    /// <summary>Draws sprite if present, else glyph if non-empty. Returns whether anything was actually drawn. outline defaults false -- only MapWindow's own entity/terrain draws pass true, since those glyphs sit over arbitrary terrain colors; Folder/inventory item-cell icons sit on a fixed window background and don't need it.</summary>
    public static bool Draw(
        SpriteBatch spriteBatch,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer,
        LabelRenderer labelRenderer,
        SpriteComponent? sprite,
        SpriteFontBase glyphFont,
        string glyph,
        Color glyphColor,
        Vector2 topLeft,
        Vector2 size,
        Color spriteTint,
        float alphaMultiplier = 1f,
        bool outline = false)
    {
        if (sprite is { } spriteComponent)
        {
            var texture = spriteSheetService.GetTexture(spriteComponent.SheetPath);
            spriteRenderer.Draw(spriteBatch, texture, spriteComponent.SourceRectangle, topLeft, size, spriteTint * alphaMultiplier);
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
