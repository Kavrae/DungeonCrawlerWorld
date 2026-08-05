using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>Generic shadowed-text draw primitive -- HotbarContent's potion-cooldown countdown, PlayerStatusEffectsContent's own countdown, and PlayerStatusEffectsContent's status-effect stack counts all draw a small number the same "colored digits over a 1px black drop shadow" way, just at different positions.</summary>
public static class ShadowedTextRenderer
{
    public static readonly Color TextColor = Color.LightGreen;
    public static readonly Color ShadowColor = Color.Black;

    private static readonly Vector2 ShadowOffset = new(-1, -1);

    /// <summary>position is the text's own top-left -- callers differ only in where they place it (HotbarContent centers above a slot, PlayerStatusEffectsContent centers below an icon).</summary>
    public static void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 position)
    {
        spriteBatch.DrawString(font, text, position + ShadowOffset, ShadowColor);
        spriteBatch.DrawString(font, text, position, TextColor);
    }
}
