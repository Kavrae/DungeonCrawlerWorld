using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>Draws an X: two lines corner to corner across a rectangle, each centred on its diagonal.</summary>
public static class CrossRenderer
{
    /// <param name="inset">How far the lines' ends sit in from the area's edges.</param>
    public static void Draw<TTarget>(TTarget spriteBatch, Vector2 areaTopLeft, Vector2 areaSize, float inset, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var left = areaTopLeft.X + inset;
        var top = areaTopLeft.Y + inset;
        var right = areaTopLeft.X + areaSize.X - inset;
        var bottom = areaTopLeft.Y + areaSize.Y - inset;

        StrokeRenderer.Line(spriteBatch, new Vector2(left, top), new Vector2(right, bottom), thickness, color, style);
        StrokeRenderer.Line(spriteBatch, new Vector2(right, top), new Vector2(left, bottom), thickness, color, style);
    }
}
