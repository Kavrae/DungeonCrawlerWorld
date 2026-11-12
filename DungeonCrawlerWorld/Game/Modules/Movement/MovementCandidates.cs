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

    /// <summary>How long a Random-mode entity waits before retrying after finding every direction blocked -- turned into an absolute MovementComponent.WaitUntilFrame at the moment the search fails (see TestCombatBehaviorSystem.SetIdle).</summary>
    public const ushort FramesToWaitIfNoOptions = 120;

    /// <summary> Determines whether an entity of the given size could occupy the given position. </summary>
    /// <remarks>A cell whose structure or floor blocks movement stops every mover except a Phasing one; a Blocking occupant stops only Blocking movers.</remarks>
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

        if (!isBlocking && mapQuery.IsPhasing(entityId))
        {
            return true;
        }

        if (size == TransformSize1)
        {
            return CanOccupyCell(mapQuery, position, entityId, isBlocking);
        }

        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                if (!CanOccupyCell(mapQuery, new Vector3Int(x, y, position.Z), entityId, isBlocking))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool CanOccupyCell(IMapQuery mapQuery, Vector3Int cell, int entityId, bool isBlocking)
    {
        if (mapQuery.IsCellBlocked(cell))
        {
            return false;
        }

        if (!isBlocking)
        {
            return true;
        }

        var occupyingEntityId = mapQuery.GetEntityIdAt(cell);
        return occupyingEntityId == -1 || occupyingEntityId == entityId;
    }

    /// <summary>Whether an entity at oldPosition may step to newPosition: the one rule every step is taken, written and offered by.</summary>
    /// <remarks>newPosition must be one of the eight tiles around oldPosition on its layer, a tile the entity can occupy, and, for a diagonal, not a cut through a corner whose two flanking tiles are both closed. A move any longer is a teleport (EntityTeleporter), never a step.</remarks>
    public static bool CanStep(IMapQuery mapQuery, Vector3Int oldPosition, Vector3Int newPosition, Vector2Byte size, int entityId, bool isBlocking) =>
        IsOneTileStep(oldPosition, newPosition) &&
        CanOccupy(mapQuery, newPosition, size, entityId, isBlocking) &&
        IsDiagonalMoveClear(mapQuery, oldPosition, newPosition, size, entityId, isBlocking);

    /// <summary>Whether newPosition is one of the eight tiles around oldPosition on the same layer.</summary>
    public static bool IsOneTileStep(Vector3Int oldPosition, Vector3Int newPosition) =>
        oldPosition.Z == newPosition.Z &&
        oldPosition != newPosition &&
        (uint)(newPosition.X - oldPosition.X + 1) <= 2u &&
        (uint)(newPosition.Y - oldPosition.Y + 1) <= 2u;

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
        Span<Direction> remaining = [Direction.North, Direction.South, Direction.West, Direction.East];
        var remainingCount = 4;

        while (remainingCount > 0)
        {
            var pickIndex = remainingCount == 1 ? 0 : mathUtility.Next(0, remainingCount);
            var direction = remaining[pickIndex];

            var positionToTest = direction switch
            {
                Direction.North => new Vector3Int(position.X, position.Y - 1, position.Z),
                Direction.South => new Vector3Int(position.X, position.Y + 1, position.Z),
                Direction.West => new Vector3Int(position.X - 1, position.Y, position.Z),
                Direction.East => new Vector3Int(position.X + 1, position.Y, position.Z),
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
}
