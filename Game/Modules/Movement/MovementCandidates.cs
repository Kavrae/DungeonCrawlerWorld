using Engine.Math;
using Game.Modules.Movement.Systems;
using Game.World;

namespace Game.Modules.Movement;

/// <summary>
/// Shared position-candidate math for Random-mode wandering and move re-validation -- used by
/// MovementSystem (re-validating an already-queued move) and TestCombatBehaviorSystem (deciding
/// a new wander destination, in a different module). Pure functions over IMapQuery/MathUtility,
/// no component-pool writes of their own -- callers decide what to do with the answer.
/// </summary>
public static class MovementCandidates
{
    /// <summary>Mirrors MovementSystem's old FramesToWaitIfNoOptions -- how long a Random-mode entity waits before retrying after finding every direction blocked. Going idle isn't an action, so callers apply this to MovementComponent.FramesToWait, not the shared ActionLockComponent.</summary>
    public const short FramesToWaitIfNoOptions = 120;

    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    /// <summary>
    /// Whether an entity of the given X/Y size could occupy the given position: every cell in
    /// its footprint must be on the map (see IMapQuery.IsOnMap(Vector3Int, Vector2Byte)) and
    /// either unoccupied or already occupied by itself. Bounds are checked first, since they
    /// have to be checked regardless and an out-of-bounds position never needs the occupancy
    /// work at all. Occupancy itself still has to be checked per cell (unlike bounds, a
    /// cell's occupancy can't be inferred from its neighbors' occupancy). isBlocking (see
    /// IMapQuery.IsBlocking) is the caller's to compute once and pass in, not this method's --
    /// it depends only on entityId, not on the candidate position being tested, so
    /// TryPickRandomAdjacentPosition's retry loop would otherwise recompute the identical answer
    /// on every candidate direction it tries for the same entity. A non-Blocking mover skips the
    /// occupancy comparison entirely -- it's exempt from map collision, the same reason it
    /// never occupies the map's occupancy index in the first place (see World.IsBlocking) --
    /// but still can't move off the map.
    /// </summary>
    public static bool CanOccupy(IMapQuery mapQuery, Vector3Int position, Vector2Byte size, int entityId, bool isBlocking)
    {
        if (!mapQuery.IsOnMap(position, size))
        {
            return false;
        }

        if (!isBlocking)
        {
            return true;
        }

        if (size == TransformSize1)
        {
            var occupyingEntityId = mapQuery.GetEntityIdAt(position);
            return occupyingEntityId == -1 || occupyingEntityId == entityId;
        }

        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                var occupyingEntityId = mapQuery.GetEntityIdAt(new Vector3Int(x, y, position.Z));
                if (occupyingEntityId != -1 && occupyingEntityId != entityId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a diagonal step from oldPosition to newPosition is legal -- rejects cutting
    /// through a wall corner by requiring at least one of the two flanking orthogonal tiles
    /// (the ones a straight N/S and E/W step from oldPosition would land on) to itself be
    /// occupiable. Callers only need this for an actual diagonal delta (both axes nonzero);
    /// for a cardinal move flanking tiles don't apply, so this always returns true then.
    /// </summary>
    public static bool IsDiagonalMoveClear(IMapQuery mapQuery, Vector3Int oldPosition, Vector3Int newPosition, Vector2Byte size, int entityId, bool isBlocking)
    {
        var deltaX = newPosition.X - oldPosition.X;
        var deltaY = newPosition.Y - oldPosition.Y;
        if (deltaX == 0 || deltaY == 0)
        {
            return true;
        }

        var horizontalFlank = new Vector3Int(newPosition.X, oldPosition.Y, oldPosition.Z);
        var verticalFlank = new Vector3Int(oldPosition.X, newPosition.Y, oldPosition.Z);

        return CanOccupy(mapQuery, horizontalFlank, size, entityId, isBlocking) ||
            CanOccupy(mapQuery, verticalFlank, size, entityId, isBlocking);
    }

    /// <summary>
    /// Picks a random neighboring node the entity could occupy, retrying up to 3 more directions
    /// on failure -- mirrors MovementSystem's old SetRandomMapPosition, minus the
    /// MovementComponent.NextMapPosition write/idle-fallback side effects (the caller owns
    /// those). Directions immediately after the first failed attempt are slightly more likely to
    /// be selected than a uniform choice would give (see MathUtility.RandomExceptFor). Returns
    /// false if every direction is blocked or off-map.
    /// </summary>
    public static bool TryPickRandomAdjacentPosition(IMapQuery mapQuery, MathUtility mathUtility, int entityId, Vector3Int position, Vector2Byte size, bool isBlocking, out Vector3Int candidatePosition)
    {
        var positionToTest = new Vector3Int();
        Span<int> failedIndexes = stackalloc int[4];
        var failedIndexCount = 0;

        if (position.Y == 0)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.North;
        }
        else if (position.Y == mapQuery.MapSize.Y - size.Y)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.South;
        }
        if (position.X == 0)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.East;
        }
        else if (position.X == mapQuery.MapSize.X - size.X)
        {
            failedIndexes[failedIndexCount++] = (int)Direction.West;
        }

        do
        {
            var randomDirection = (Direction)mathUtility.RandomExceptFor(4, failedIndexes[..failedIndexCount]);
            positionToTest = randomDirection switch
            {
                Direction.North => new Vector3Int(position.X, position.Y - 1, position.Z),
                Direction.South => new Vector3Int(position.X, position.Y + 1, position.Z),
                Direction.East => new Vector3Int(position.X - 1, position.Y, position.Z),
                Direction.West => new Vector3Int(position.X + 1, position.Y, position.Z),
                _ => positionToTest,
            };

            if (CanOccupy(mapQuery, positionToTest, size, entityId, isBlocking))
            {
                candidatePosition = positionToTest;
                return true;
            }

            failedIndexes[failedIndexCount++] = (int)randomDirection;
        }
        while (failedIndexCount < 4);

        candidatePosition = default;
        return false;
    }
}
