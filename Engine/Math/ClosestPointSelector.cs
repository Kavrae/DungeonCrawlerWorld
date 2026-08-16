namespace Engine.Math;

/// <summary>Provides methods for selecting the closest point based on given criteria.</summary>
public static class ClosestPointSelector
{
    /// <summary>Selects the candidate closest to a primary point, breaking a tie by distance to a secondary point.</summary>
    /// <remarks>
    /// Pure Manhattan-distance comparison over a caller-supplied candidate list.
    /// </remarks>
    /// <cleanupVersion>1</cleanupVersion>
    public static Vector3Int? SelectClosest(Vector3Int primary, Vector3Int secondary, IReadOnlyList<Vector3Int> candidates)
    {
        Vector3Int? best = null;
        var bestPrimaryDistance = int.MaxValue;
        var bestSecondaryDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var primaryDistance = GridDistance.ManhattanDistance(primary, candidate);
            var secondaryDistance = GridDistance.ManhattanDistance(secondary, candidate);

            if (primaryDistance < bestPrimaryDistance ||
                (primaryDistance == bestPrimaryDistance && secondaryDistance < bestSecondaryDistance))
            {
                best = candidate;
                bestPrimaryDistance = primaryDistance;
                bestSecondaryDistance = secondaryDistance;
            }
        }

        return best;
    }
}
