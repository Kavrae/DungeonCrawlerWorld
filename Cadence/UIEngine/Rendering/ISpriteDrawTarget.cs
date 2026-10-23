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

    void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style);

    /// <summary>text in fillColor with a one-pixel black outline, fill and outline faded together by alphaMultiplier -- ContrastTextRenderer's look, drawn however the target draws it best.</summary>
    /// <param name="position">Already a whole pixel (ContrastTextRenderer rounds it).</param>
    void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style);
}
