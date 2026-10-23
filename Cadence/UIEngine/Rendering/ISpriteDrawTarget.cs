using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>Where a sprite, a rectangle or a string is drawn: a SpriteBatch (SpriteBatchDrawTarget) or SpriteEffectBatch.</summary>
/// <remarks>The drawing helpers take it as a generic argument constrained to this interface, so a SpriteBatchDrawTarget is never boxed.</remarks>
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

/// <summary>A SpriteBatch as a draw target: every draw goes straight to it, and the style is ignored.</summary>
public readonly struct SpriteBatchDrawTarget(SpriteBatch spriteBatch) : ISpriteDrawTarget
{
    public void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style) =>
        spriteBatch.Draw(texture, destination, source, color);

    public void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style) =>
        spriteBatch.Draw(texture, position, source, color, rotation, origin, scale, effects, 0f);

    public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style) =>
        spriteBatch.DrawString(font, text, position, color);

    /// <summary>Nine draws: the outline as eight black copies a pixel apart, then the fill (ContrastTextRenderer.DrawCopies).</summary>
    public void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style) =>
        ContrastTextRenderer.DrawCopies(this, font, text, position, fillColor, alphaMultiplier, style);
}
