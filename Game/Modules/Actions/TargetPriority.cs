using Engine.Math;

namespace Game.Modules.Actions;

/// <summary>
/// Picks the best auto-target among a caller-supplied set of candidate tiles, for double-tap
/// activation. Candidates are expected to already be filtered to occupied, in-range tiles by
/// the caller (see MapWindow's double-tap handling) -- this is purely a "which of these is
/// best" comparison, not an occupancy/range query itself.
///
/// Priority is lexicographic: closest to the cursor first, closest to the attacker as a
/// tiebreaker. Until real cursor-hover tracking exists (a later Presentation phase), callers
/// pass cursorTile == attackerPosition, which makes the two keys identical -- "closest to
/// cursor" degenerates to "closest to the attacker" for now, with no code change needed once a
/// real cursor position is threaded through.
/// </summary>
public static class TargetPriority
{
    public static Vector3Int? SelectAutoTarget(Vector3Int attackerPosition, Vector3Int cursorTile, IReadOnlyList<Vector3Int> candidateTiles)
    {
        Vector3Int? best = null;
        var bestCursorDistance = int.MaxValue;
        var bestAttackerDistance = int.MaxValue;

        foreach (var candidate in candidateTiles)
        {
            var cursorDistance = DistanceFalloff.ManhattanDistance(cursorTile, candidate);
            var attackerDistance = DistanceFalloff.ManhattanDistance(attackerPosition, candidate);

            if (cursorDistance < bestCursorDistance ||
                (cursorDistance == bestCursorDistance && attackerDistance < bestAttackerDistance))
            {
                best = candidate;
                bestCursorDistance = cursorDistance;
                bestAttackerDistance = attackerDistance;
            }
        }

        return best;
    }
}
