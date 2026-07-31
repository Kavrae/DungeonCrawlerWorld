using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>
/// Draws a spritesheet region stretched to fill an arbitrary pixel footprint. Decoupled from
/// Map/World/ComponentManager -- callers (e.g. MapWindow) resolve which entity/texture/
/// source-region/position/tint to use and pass those in as plain values, mirroring
/// GlyphRenderer's role for text.
/// </summary>
public sealed class SpriteRenderer
{
    public void Draw(SpriteBatch spriteBatch, Texture2D texture, Rectangle sourceRectangle, Vector2 footprintTopLeft, Vector2 footprintSize, Color tint)
    {
        var destinationRectangle = new Rectangle((int)footprintTopLeft.X, (int)footprintTopLeft.Y, (int)footprintSize.X, (int)footprintSize.Y);
        spriteBatch.Draw(texture, destinationRectangle, sourceRectangle, tint);
    }
}
