using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>Where a sprite, a rectangle or a string is drawn: SpriteEffectBatch, or a test's recording target.</summary>
/// <remarks>The drawing helpers take it as a generic argument constrained to this interface, so a struct target is never boxed; SpriteDrawTargetExtensions adds SpriteBatch's call shapes to any of them.</remarks>
public interface ISpriteDrawTarget
{
    void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style);

    /// <param name="origin">In source pixels, as SpriteBatch takes it: what position names and rotation turns about.</param>
    void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style);

    /// <summary>Fills destination with color: a rectangle of one solid colour, shaded by style.</summary>
    /// <remarks>The target brings its own white texel, so nothing that draws a rectangle carries a texture for it.</remarks>
    void FillRectangle(Rectangle destination, Color color, SpriteDrawStyle style);

    /// <summary>Fills a size-by-size rectangle turned by rotation about normalizedOrigin, which sits at position -- a line at an angle.</summary>
    /// <param name="normalizedOrigin">The point of the rectangle position names and rotation turns about, as a fraction of its size.</param>
    void FillRectangle(Vector2 position, Vector2 size, float rotation, Vector2 normalizedOrigin, Color color, SpriteDrawStyle style);

    void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style);

    /// <summary>text in fillColor with a one-pixel black outline, fill and outline faded together by alphaMultiplier -- ContrastTextRenderer's look, drawn however the target draws it best.</summary>
    /// <param name="position">Already a whole pixel (ContrastTextRenderer rounds it).</param>
    void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style);
}
