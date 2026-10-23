using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Presentation.Rendering;

/// <summary>Something on the map resolved for drawing: its sprite if the manifest has one, otherwise its glyph, and whether it is a corpse.</summary>
/// <remarks>A sprite always wins when set. Glyph may still be set beside it -- an entity's appearance declares both -- for a reader that only ever draws glyphs.</remarks>
public readonly record struct MapVisual(SpriteView? Sprite, string Glyph, Color GlyphColor, bool IsDead = false)
{
    /// <summary>How the sprite effect shades it: a corpse desaturated, in its own colours, so it stays as light or dark as it was alive.</summary>
    public SpriteDrawStyle DrawStyle => IsDead ? SpriteDrawStyle.Desaturated : SpriteDrawStyle.Plain;
}
