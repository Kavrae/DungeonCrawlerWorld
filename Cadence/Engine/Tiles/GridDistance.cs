using Engine.Math;

namespace Engine.Tiles;

/// <summary>Grid-distance calculations between two map positions.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class GridDistance
{
    /// <summary>Calculates the Chebyshev distance between two map positions.</summary>
    /// <remarks>Diagonals cost the same as orthogonal moves.</remarks>
    /// <param name="a">The first map position.</param>
    /// <param name="b">The second map position.</param>
    /// <returns>The Chebyshev distance between the two positions.</returns>
    public static int ChebyshevDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>Calculates the Chebyshev distance between the nearest tiles of two footprints.</summary>
    /// <remarks>A footprint covers its origin and Size tiles toward +X and +Y; overlapping footprints are 0 apart.</remarks>
    /// <param name="aOrigin">The first footprint's origin tile.</param>
    /// <param name="aSize">The first footprint's size in tiles.</param>
    /// <param name="bOrigin">The second footprint's origin tile.</param>
    /// <param name="bSize">The second footprint's size in tiles.</param>
    /// <returns>The Chebyshev distance between the footprints' nearest tiles.</returns>
    public static int ChebyshevDistance(Vector3Int aOrigin, Vector2Byte aSize, Vector3Int bOrigin, Vector2Byte bSize) =>
        System.Math.Max(
            GapBetweenSpans(aOrigin.X, aSize.X, bOrigin.X, bSize.X),
            GapBetweenSpans(aOrigin.Y, aSize.Y, bOrigin.Y, bSize.Y));

    private static int GapBetweenSpans(int aStart, int aLength, int bStart, int bLength) =>
        System.Math.Max(0, System.Math.Max(aStart - (bStart + bLength - 1), bStart - (aStart + aLength - 1)));

    /// <summary>Whether an offset of (deltaX, deltaY) tiles lies within a rounded circle of the given radius.</summary>
    /// <remarks>
    /// The one distance targeting measures range and area by: a tile is inside when its centre is within radius + 1/2 of the
    /// origin's centre, so deltaX^2 + deltaY^2 &lt;= radius^2 + radius. Radius 1 is the 3x3 block around the origin, and larger
    /// radii round off the square's corners. A negative radius holds nothing.
    /// </remarks>
    /// <param name="deltaX">The offset along X, in tiles.</param>
    /// <param name="deltaY">The offset along Y, in tiles.</param>
    /// <param name="radius">The circle's radius, in tiles.</param>
    /// <returns>Whether the offset is inside the circle.</returns>
    public static bool IsWithinRoundedRadius(int deltaX, int deltaY, int radius) =>
        radius >= 0 && (long)deltaX * deltaX + (long)deltaY * deltaY <= (long)radius * radius + radius;

    /// <summary>Whether b lies within a rounded circle of the given radius around a.</summary>
    /// <remarks>See IsWithinRoundedRadius(int, int, int).</remarks>
    /// <param name="a">The circle's centre.</param>
    /// <param name="b">The map position tested.</param>
    /// <param name="radius">The circle's radius, in tiles.</param>
    /// <returns>Whether b is inside the circle.</returns>
    public static bool IsWithinRoundedRadius(Vector3Int a, Vector3Int b, int radius) =>
        IsWithinRoundedRadius(b.X - a.X, b.Y - a.Y, radius);

    /// <summary>Whether tile lies within a rounded circle of the given radius around the nearest tile of a footprint.</summary>
    /// <remarks>A footprint covers its origin and Size tiles toward +X and +Y; a tile inside it is at distance 0. See IsWithinRoundedRadius(int, int, int).</remarks>
    /// <param name="footprintOrigin">The footprint's origin tile.</param>
    /// <param name="footprintSize">The footprint's size in tiles.</param>
    /// <param name="tile">The map position tested.</param>
    /// <param name="radius">The circle's radius, in tiles.</param>
    /// <returns>Whether tile is inside the rounded radius of the footprint.</returns>
    public static bool IsWithinRoundedRadius(Vector3Int footprintOrigin, Vector2Byte footprintSize, Vector3Int tile, int radius) =>
        IsWithinRoundedRadius(
            GapBetweenSpans(footprintOrigin.X, footprintSize.X, tile.X, 1),
            GapBetweenSpans(footprintOrigin.Y, footprintSize.Y, tile.Y, 1),
            radius);

    /// <summary>Calculates the Manhattan distance between two map positions.</summary>
    /// <remarks>Diagonals cost twice as much as orthogonal moves.</remarks>
    /// <param name="a">The first map position.</param>
    /// <param name="b">The second map position.</param>
    /// <returns>The Manhattan distance between the two positions.</returns>
    public static int ManhattanDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);
}
