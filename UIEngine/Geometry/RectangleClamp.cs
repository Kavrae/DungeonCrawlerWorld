using Microsoft.Xna.Framework;

namespace UIEngine.Geometry;

/// <summary>Moves a rectangle the least distance that puts it inside another.</summary>
/// <remarks>
/// The rectangle is given as a position and size, so a float position is kept as it is. One larger
/// than its bounds on an axis is pinned to the bounds' minimum edge on that axis.
/// </remarks>
public static class RectangleClamp
{
    /// <summary>Returns the position of a rectangle of size, moved to lie inside bounds.</summary>
    public static Vector2 ClampInside(Vector2 position, Vector2 size, Rectangle bounds) =>
        ClampInside(position, size, new Vector2(bounds.X, bounds.Y), new Vector2(bounds.Width, bounds.Height));

    /// <summary>Returns the position of a rectangle of size, moved to lie inside bounds that start at the origin.</summary>
    public static Vector2 ClampInside(Vector2 position, Vector2 size, Vector2 boundsSize) =>
        ClampInside(position, size, Vector2.Zero, boundsSize);

    private static Vector2 ClampInside(Vector2 position, Vector2 size, Vector2 boundsPosition, Vector2 boundsSize) => new(
        ClampAxis(position.X, size.X, boundsPosition.X, boundsSize.X),
        ClampAxis(position.Y, size.Y, boundsPosition.Y, boundsSize.Y));

    private static float ClampAxis(float position, float size, float boundsMinimum, float boundsLength) =>
        MathHelper.Clamp(position, boundsMinimum, boundsMinimum + MathHelper.Max(0, boundsLength - size));
}
