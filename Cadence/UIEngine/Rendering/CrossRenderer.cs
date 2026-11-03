using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>Draws an X: two lines corner to corner across a rectangle, each centred on its diagonal.</summary>
/// <remarks>A line is unitRectangle stretched into a thickness-tall strip and turned to match -- the renderer has no line primitive.</remarks>
public static class CrossRenderer
{
    /// <param name="inset">How far the lines' ends sit in from the area's edges.</param>
    public static void Draw(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 areaTopLeft, Vector2 areaSize, float inset, float thickness, Color color, SpriteDrawStyle style = default)
    {
        var left = areaTopLeft.X + inset;
        var top = areaTopLeft.Y + inset;
        var right = areaTopLeft.X + areaSize.X - inset;
        var bottom = areaTopLeft.Y + areaSize.Y - inset;

        DrawLine(spriteBatch, unitRectangle, new Vector2(left, top), new Vector2(right, bottom), thickness, color, style);
        DrawLine(spriteBatch, unitRectangle, new Vector2(right, top), new Vector2(left, bottom), thickness, color, style);
    }

    private static void DrawLine(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 start, Vector2 end, float thickness, Color color, SpriteDrawStyle style)
    {
        var delta = end - start;
        var rotation = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(unitRectangle, start, null, color, rotation, new Vector2(0f, 0.5f), new Vector2(delta.Length(), thickness), SpriteEffects.None, style);
    }
}
