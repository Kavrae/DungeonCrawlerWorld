using Engine.Math;
using Game.Modules.Movement.Systems;
using Game.World;

namespace Game.Modules.Movement;

/// <summary> Shared position-candidate math for Random-mode wandering and move re-validation </summary>
/// <remarks>Used by MovementSystem (re-validating an already-queued move) and TestCombatBehaviorSystem (deciding
/// a new wander destination, in a different module). Pure functions over IMapQuery/MathUtility,
/// no component-pool writes of their own -- callers decide what to do with the answer.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class MovementCandidates
{
    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    /// <summary>How long a Random-mode entity waits before retrying after finding every direction blocked.</summary>
    public const ushort FramesToWaitIfNoOptions = 120;

    /// <summary> Determines whether an entity of the given size could occupy the given position. </summary>
    /// <remarks> Blocking entities can always occupy a space. </remarks>
    /// <param name="mapQuery">The map query.</param>
    /// <param name="position">The position to check.</param>
    /// <param name="size">The size of the entity.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="isBlocking">Indicates whether the entity is blocking.</param>
    /// <returns>True if the space can be occupied by the entity.</returns>
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

    /// <summary>Determines whether a diagonal step from oldPosition to newPosition is legal.</summary>
    /// <remarks>Requires at least one of the two flanking orthogonal tiles to be occupiable.</remarks>
    /// <param name="mapQuery">The map query.</param>
    /// <param name="oldPosition">The old position.</param>
    /// <param name="newPosition">The new position.</param>
    /// <param name="size">The size of the entity.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="isBlocking">Indicates whether the entity is blocking.</param>
    /// <returns>True if the diagonal move is clear.</returns>
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

    /// <summary>Tries to pick a random adjacent position that the entity can occupy.</summary>
    /// <param name="mapQuery">The map query.</param>
    /// <param name="mathUtility">The math utility.</param>
    /// <param name="entityId">The ID of the entity.</param>
    /// <param name="position">The current position.</param>
    /// <param name="size">The size of the entity.</param>
    /// <param name="isBlocking">Indicates whether the entity is blocking.</param>
    /// <param name="candidatePosition">The candidate position.</param>
    /// <returns>True if a valid position was found.</returns>
    public static bool TryPickRandomAdjacentPosition(IMapQuery mapQuery, MathUtility mathUtility, int entityId, Vector3Int position, Vector2Byte size, bool isBlocking, out Vector3Int candidatePosition)
    {
        Span<Direction> remaining = [Direction.North, Direction.South, Direction.East, Direction.West];
        var remainingCount = 4;

        if (position.Y == 0)
        {
            RemoveDirection(remaining, ref remainingCount, Direction.North);
        }
        else if (position.Y == mapQuery.MapSize.Y - size.Y)
        {
            RemoveDirection(remaining, ref remainingCount, Direction.South);
        }
        if (position.X == 0)
        {
            RemoveDirection(remaining, ref remainingCount, Direction.East);
        }
        else if (position.X == mapQuery.MapSize.X - size.X)
        {
            RemoveDirection(remaining, ref remainingCount, Direction.West);
        }

        while (remainingCount > 0)
        {
            var pickIndex = remainingCount == 1 ? 0 : mathUtility.Next(0, remainingCount);
            var direction = remaining[pickIndex];

            var positionToTest = direction switch
            {
                Direction.North => new Vector3Int(position.X, position.Y - 1, position.Z),
                Direction.South => new Vector3Int(position.X, position.Y + 1, position.Z),
                Direction.East => new Vector3Int(position.X - 1, position.Y, position.Z),
                Direction.West => new Vector3Int(position.X + 1, position.Y, position.Z),
                _ => position,
            };

            if (CanOccupy(mapQuery, positionToTest, size, entityId, isBlocking))
            {
                candidatePosition = positionToTest;
                return true;
            }

            remaining[pickIndex] = remaining[--remainingCount];
        }

        candidatePosition = default;
        return false;
    }

    /// <summary> Swap-removes direction from the remaining candidate set, if present. </summary>
    private static void RemoveDirection(Span<Direction> remaining, ref int remainingCount, Direction direction)
    {
        for (var i = 0; i < remainingCount; i++)
        {
            if (remaining[i] == direction)
            {
                remaining[i] = remaining[--remainingCount];
                return;
            }
        }
    }
}
