using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>SpriteBatch's Draw and DrawString shapes on any ISpriteDrawTarget, each with an optional style (Plain by default), so drawing reads as FNA code does.</summary>
/// <remarks>Each is SpriteBatch's own overload without the layer depth (a Deferred batch draws in call order and ignores it), mapped onto the target's two Draw members as SpriteBatch maps them. Generic, so a struct target is never boxed.</remarks>
public static class SpriteDrawTargetExtensions
{
    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Rectangle destination, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, destination, null, color, style);

    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Rectangle destination, Rectangle? source, Color color)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, destination, source, color, SpriteDrawStyle.Plain);

    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Vector2 position, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, position, null, color, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, style);

    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Vector2 position, Rectangle? source, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, position, source, color, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, style);

    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, position, source, color, rotation, origin, new Vector2(scale), effects, style);

    public static void Draw<TTarget>(this TTarget target, Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects)
        where TTarget : ISpriteDrawTarget =>
        target.Draw(texture, position, source, color, rotation, origin, scale, effects, SpriteDrawStyle.Plain);

    public static void DrawString<TTarget>(this TTarget target, SpriteFontBase font, string text, Vector2 position, Color color)
        where TTarget : ISpriteDrawTarget =>
        target.DrawString(font, text, position, color, SpriteDrawStyle.Plain);
}
