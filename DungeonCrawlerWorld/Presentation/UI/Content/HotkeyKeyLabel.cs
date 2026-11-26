using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Presentation.UI.Content;

/// <summary>A hotbar slot's key, drawn over the top-left of an icon: on the slot itself, and on every Actions or Inventory cell showing what a slot is bound to.</summary>
public static class HotkeyKeyLabel
{
    /// <summary>How far in from the icon's top-left corner the key sits.</summary>
    public static readonly Vector2 Padding = new(4f, 2f);

    public static void Draw(SpriteEffectBatch spriteBatch, SpriteFontBase font, string keyLabel, Rectangle bounds, float alpha = 1f) =>
        ContrastTextRenderer.Draw(spriteBatch, font, keyLabel, new Vector2(bounds.X + Padding.X, bounds.Y + Padding.Y), alpha);
}
