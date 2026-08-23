using FontStashSharp;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// A cursor-following copy of a dragged item's icon -- DragGhostContent draws this once, in the
/// User tier (topmost of Base/StaticHud/DynamicHud/User), so it always renders on top regardless
/// of which tier the drag started or will end over. Takes an already-resolved sprite/glyph (the
/// same split SpriteOrGlyphRenderer itself uses) rather than an ItemDefinition, so this stays as
/// ignorant of Game-layer catalog lookups as every other renderer in this namespace --
/// DragGhostContent does that resolution itself.
/// </summary>
public static class DragGhostRenderer
{
    /// <summary>Slightly transparent -- reads as "not yet placed" rather than a fully solid icon, the same affordance most desktop drag-and-drop ghosts use.</summary>
    private const float Alpha = 0.85f;

    public static void Draw(
        SpriteBatch spriteBatch,
        SpriteSheetService spriteSheetService,
        SpriteRenderer spriteRenderer,
        LabelRenderer labelRenderer,
        SpriteFontBase glyphFont,
        SpriteComponent? sprite,
        string glyph,
        Color glyphColor,
        Vector2 centerPosition,
        Vector2 size)
    {
        var topLeft = centerPosition - size / 2f;
        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, labelRenderer, sprite, glyphFont, glyph, glyphColor, topLeft, size, Color.White, Alpha);
    }
}
