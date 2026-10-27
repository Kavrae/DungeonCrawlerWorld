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

    /// <summary>Calculates the Manhattan distance between two map positions.</summary>
    /// <remarks>Diagonals cost twice as much as orthogonal moves.</remarks>
    /// <param name="a">The first map position.</param>
    /// <param name="b">The second map position.</param>
    /// <returns>The Manhattan distance between the two positions.</returns>
    public static int ManhattanDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);
}
