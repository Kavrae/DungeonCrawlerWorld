namespace Engine.Math;

/// <summary>Provides methods for calculating distance-based falloff values.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class DistanceFalloff
{
    /// <summary>Strength halved per tile of distance from the center, floored at 0. </summary>
    public static int ValueAtDistance(int strength, int distance) =>
        distance < 0 ? 0 : strength >> distance;

    /// <summary>Furthest tile at which ValueAtDistance(strength, ...) is still > 0 </summary>
    /// <remarks>-1 if strength itself is <= 0.</remarks>
    public static int MaxRadius(int strength) => strength <= 0 ? -1 : (int)System.Math.Log2(strength);

    /// <summary>Represents a delegate for visiting cells within a Manhattan distance.</summary>
    /// <param name="cellPosition">The position of the cell being visited.</param>
    /// <param name="contribution">The contribution value for the cell.</param>
    public delegate void ManhattanCellVisitor(Vector3Int cellPosition, int contribution);

    /// <summary>Represents a delegate for visiting cells within a Manhattan distance, with a state parameter.</summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="cellPosition">The position of the cell being visited.</param>
    /// <param name="contribution">The contribution value for the cell.</param>
    /// <param name="state">The state parameter.</param>
    public delegate void ManhattanCellVisitor<in TState>(Vector3Int cellPosition, int contribution, TState state);

    /// <summary> Visits every cell within radius tiles of sourcePosition, calling visit for each visited cell whose contribution is greater than 0.
    /// </summary>
    /// <remarks>
    /// Same Z layer as the sourcePosition, clamped to a mapSize.X x mapSize.Y grid.
    /// Thin wrapper over the TState overload -- existing callers here already pass a
    /// capturing lambda (all current ones run once at construction/toggle time, or once per
    /// preview-frame into a reused results list, not allocating per cell), so this keeps their
    /// call sites unchanged rather than forcing every caller to adopt the state-passing shape.
    /// </remarks>
    /// <param name="sourcePosition">The source position from which to scatter.</param>
    /// <param name="radius">The radius within which to scatter.</param>
    /// <param name="strength">The strength of the effect in the scatter.</param>
    /// <param name="shape">The falloff shape as either Flat or Fading at half strength per cell distance.</param>
    /// <param name="mapSize">The size of the map for bounds checking.</param>
    /// <param name="visit">The visitor delegate to be called on each cell within range.</param>
    public static void ScatterManhattan(Vector3Int sourcePosition, int radius, int strength, FalloffShape shape, Vector3Int mapSize, ManhattanCellVisitor visit) =>
        ScatterManhattan(sourcePosition, radius, strength, shape, mapSize, visit, static (cellPosition, contribution, state) => state(cellPosition, contribution));

    /// <summary>Visits every cell within radius tiles of sourcePosition, calling visit with the given state for each visited cell whose contribution is greater than 0.</summary>
    /// <typeparam name="TState">The type of the state parameter.</typeparam>
    /// <param name="sourcePosition">The source position from which to scatter.</param>
    /// <param name="radius">The radius within which to scatter.</param>
    /// <param name="strength">The strength of the effect in the scatter.</param>
    /// <param name="shape">The falloff shape as either Flat or Fading at half strength per cell distance.</param>
    /// <param name="mapSize">The size of the map for bounds checking.</param>
    /// <param name="state">The state to provide to each visit call.</param>
    /// <param name="visit">The visitor delegate to be called on each cell within range.</param>
    public static void ScatterManhattan<TState>(Vector3Int sourcePosition, int radius, int strength, FalloffShape shape, Vector3Int mapSize, TState state, ManhattanCellVisitor<TState> visit)
    {
        if (radius < 0)
        {
            return;
        }

        for (var deltaY = -radius; deltaY <= radius; deltaY++)
        {
            var cellY = sourcePosition.Y + deltaY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            var remainingRadius = radius - System.Math.Abs(deltaY);
            for (var deltaX = -remainingRadius; deltaX <= remainingRadius; deltaX++)
            {
                var cellX = sourcePosition.X + deltaX;
                if (cellX < 0 || cellX >= mapSize.X)
                {
                    continue;
                }

                var cellPosition = new Vector3Int(cellX, cellY, sourcePosition.Z);
                var contribution = shape == FalloffShape.Fading
                    ? ValueAtDistance(strength, GridDistance.ManhattanDistance(sourcePosition, cellPosition))
                    : strength;
                if (contribution <= 0)
                {
                    continue;
                }

                visit(cellPosition, contribution, state);
            }
        }
    }
}
