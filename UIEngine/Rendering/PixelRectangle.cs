using Microsoft.Xna.Framework;

namespace UIEngine.Rendering;

/// <summary>A drawn area in fractional pixels, snapped to whole pixels by its edges.</summary>
/// <remarks>
/// Each edge is floored, rather than the position and the size truncated apart, so two areas that meet at a fractional pixel still meet
/// once snapped -- no gap or overlap between neighbouring tiles at a fractional zoom -- and an area left or above the origin rounds the same
/// way as one to its right.
/// </remarks>
public static class PixelRectangle
{
    public static Rectangle Snapped(Vector2 topLeft, Vector2 size)
    {
        var left = (int)MathF.Floor(topLeft.X);
        var top = (int)MathF.Floor(topLeft.Y);
        var right = (int)MathF.Floor(topLeft.X + size.X);
        var bottom = (int)MathF.Floor(topLeft.Y + size.Y);
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
