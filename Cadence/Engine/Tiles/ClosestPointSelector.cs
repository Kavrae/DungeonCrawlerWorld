using Engine.Math;

namespace Engine.Tiles;

/// <summary>Provides methods for selecting the closest point based on given criteria.</summary>
public static class ClosestPointSelector
{
    /// <summary>Selects the candidate closest to a primary point, breaking a tie by distance to a secondary point.</summary>
    /// <remarks>
    /// Pure Manhattan-distance comparison over a caller-supplied candidate list.
    /// </remarks>
    /// <cleanupVersion>1</cleanupVersion>
    public static Vector3Int? SelectClosest(Vector3Int primary, Vector3Int secondary, ReadOnlySpan<Vector3Int> candidates)
    {
        if (candidates.IsEmpty)
        {
            return null;
        }

        var bestIndex = -1;
        var bestPrimaryDistance = int.MaxValue;
        var bestSecondaryDistance = int.MaxValue;

        for (var index = 0; index < candidates.Length; index++)
        {
            var primaryDistance = GridDistance.ManhattanDistance(primary, candidates[index]);
            if (primaryDistance > bestPrimaryDistance)
            {
                continue;
            }

            var secondaryDistance = GridDistance.ManhattanDistance(secondary, candidates[index]);
            if (primaryDistance < bestPrimaryDistance || secondaryDistance < bestSecondaryDistance)
            {
                bestIndex = index;
                bestPrimaryDistance = primaryDistance;
                bestSecondaryDistance = secondaryDistance;
            }
        }

        return candidates[bestIndex];
    }
}
