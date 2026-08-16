namespace Engine.Math;

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

    /// <summary>Calculates the Manhattan distance between two map positions.</summary>
    /// <remarks>Diagonals cost twice as much as orthogonal moves.</remarks>
    /// <param name="a">The first map position.</param>
    /// <param name="b">The second map position.</param>
    /// <returns>The Manhattan distance between the two positions.</returns>
    public static int ManhattanDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);
}
