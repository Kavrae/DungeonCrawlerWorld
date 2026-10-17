using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpriteFormat;

namespace UIEngine.Rendering;

/// <summary>
/// Draws a spritesheet region stretched to fill an arbitrary pixel footprint, turned and flipped as its
/// manifest cell says. Decoupled from any world or store -- callers resolve
/// which entity/texture/source-region/position/tint to use and pass those in as plain values, mirroring
/// LabelRenderer's role for text.
/// </summary>
/// <remarks>Both a plain and a turned or flipped cell draw into the same whole-pixel rectangle (PixelRectangle), so one never lands a pixel off the other.</remarks>
public static class SpriteRenderer
{
    public static void Draw(SpriteBatch spriteBatch, Texture2D texture, Rectangle sourceRectangle, Vector2 footprintTopLeft, Vector2 footprintSize, Color tint, SpriteCellTransform transform = default)
    {
        var destinationRectangle = PixelRectangle.Snapped(footprintTopLeft, footprintSize);
        if (transform.IsNone)
        {
            spriteBatch.Draw(texture, destinationRectangle, sourceRectangle, tint);
            return;
        }

        var arguments = TransformedDrawArguments.For(transform, sourceRectangle, new Vector2(destinationRectangle.X, destinationRectangle.Y), new Vector2(destinationRectangle.Width, destinationRectangle.Height));
        spriteBatch.Draw(texture, arguments.Position, sourceRectangle, tint, arguments.Rotation, arguments.Origin, arguments.Scale, arguments.Effects, 0f);
    }
}

/// <summary>The SpriteBatch.Draw arguments that draw a turned or flipped cell exactly over a destination rectangle.</summary>
/// <remarks>
/// Rotates about the destination's centre. SpriteBatch applies SpriteEffects to the source before rotating, which is the
/// manifest's order (flip, then turn). Scale is applied in source space before rotating, so a quarter turn takes each
/// screen axis's scale from the other source axis.
/// </remarks>
public readonly record struct TransformedDrawArguments(Vector2 Position, float Rotation, Vector2 Origin, Vector2 Scale, SpriteEffects Effects)
{
    public static TransformedDrawArguments For(SpriteCellTransform transform, Rectangle sourceRectangle, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var effects = SpriteEffects.None;
        if (transform.FlipHorizontal)
        {
            effects |= SpriteEffects.FlipHorizontally;
        }
        if (transform.FlipVertical)
        {
            effects |= SpriteEffects.FlipVertically;
        }

        var scale = transform.IsQuarterTurn
            ? new Vector2(footprintSize.Y / sourceRectangle.Width, footprintSize.X / sourceRectangle.Height)
            : new Vector2(footprintSize.X / sourceRectangle.Width, footprintSize.Y / sourceRectangle.Height);

        return new TransformedDrawArguments(
            footprintTopLeft + footprintSize / 2f,
            MathHelper.ToRadians(transform.Rotation),
            new Vector2(sourceRectangle.Width / 2f, sourceRectangle.Height / 2f),
            scale,
            effects);
    }
}
