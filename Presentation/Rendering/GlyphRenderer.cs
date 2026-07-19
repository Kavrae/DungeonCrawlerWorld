using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws a single glyph string at a position. Decoupled from Map/World/ComponentManager --
/// callers (e.g. MapWindow) resolve which entity/font/position/color to use and pass those
/// in as plain values.
/// </summary>
public sealed class GlyphRenderer
{
    public void Draw(SpriteBatch spriteBatch, SpriteFontBase font, string glyph, Vector2 position, Color color)
    {
        spriteBatch.DrawString(font, glyph, position, color);
    }
}
