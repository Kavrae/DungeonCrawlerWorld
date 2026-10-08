namespace Engine.Math;

/// <summary>Walks the cells within a Manhattan distance of a centre: a diamond, on the centre's layer.</summary>
public static class ManhattanDiamond
{
    /// <summary>Called for one visited cell with its Manhattan distance from the centre.</summary>
    public delegate void CellVisitor<in TState>(Vector3Int cellPosition, int distance, TState state);

    /// <summary>Calls visit for every cell within radius of center and inside bounds, on center's layer. A negative radius visits nothing.</summary>
    public static void ForEachCell<TState>(Vector3Int center, int radius, MapBounds bounds, TState state, CellVisitor<TState> visit) =>
        ForEachCellOutside(center, radius, bounds, excludedCenter: null, state, visit);

    /// <summary>Calls visit for every cell within radius of center and inside bounds that is not also within radius of excludedCenter on the same layer -- the cells a diamond of that radius gains by moving from excludedCenter to center.</summary>
    /// <remarks>The excluded cells are ruled out by arithmetic. A null excludedCenter excludes nothing.</remarks>
    public static void ForEachCellOutside<TState>(Vector3Int center, int radius, MapBounds bounds, Vector3Int? excludedCenter, TState state, CellVisitor<TState> visit)
    {
        if (radius < 0)
        {
            return;
        }

        var excludes = excludedCenter is { } excluded && excluded.Z == center.Z;
        var excludedX = excludedCenter?.X ?? 0;
        var excludedY = excludedCenter?.Y ?? 0;

        for (var deltaY = -radius; deltaY <= radius; deltaY++)
        {
            var cellY = center.Y + deltaY;
            if (cellY < bounds.MinY || cellY >= bounds.MaxY)
            {
                continue;
            }

            var remainingRadius = radius - System.Math.Abs(deltaY);
            for (var deltaX = -remainingRadius; deltaX <= remainingRadius; deltaX++)
            {
                var cellX = center.X + deltaX;
                if (cellX < bounds.MinX || cellX >= bounds.MaxX)
                {
                    continue;
                }

                if (excludes && System.Math.Abs(cellX - excludedX) + System.Math.Abs(cellY - excludedY) <= radius)
                {
                    continue;
                }

                visit(new Vector3Int(cellX, cellY, center.Z), System.Math.Abs(deltaX) + System.Math.Abs(deltaY), state);
            }
        }
    }
}
