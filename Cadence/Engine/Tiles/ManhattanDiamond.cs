using Engine.Math;

namespace Engine.Tiles;

/// <summary>Walks the cells within a Manhattan distance of a centre: a diamond, on the centre's layer.</summary>
public static class ManhattanDiamond
{
    /// <summary>Receives each visited cell with its Manhattan distance from the centre.</summary>
    /// <remarks>Implemented by a struct, so each walk is compiled for its visitor and the call inlined.</remarks>
    public interface ICellVisitor
    {
        void Visit(Vector3Int cellPosition, int distance);
    }

    /// <summary>Calls visitor for every cell within radius of center and inside bounds, on center's layer. A negative radius visits nothing.</summary>
    public static void ForEachCell<TVisitor>(Vector3Int center, int radius, MapBounds bounds, ref TVisitor visitor)
        where TVisitor : struct, ICellVisitor =>
        ForEachCellOutside(center, radius, bounds, excludedCenter: null, ref visitor);

    /// <summary>Calls visitor for every cell within radius of center and inside bounds that is not also within radius of excludedCenter on the same layer -- the cells a diamond of that radius gains by moving from excludedCenter to center.</summary>
    /// <remarks>
    /// Each row is clipped to bounds, and the excluded diamond's span of it is skipped, by arithmetic, so a walk costs the
    /// cells it visits and its rows, never the cells it rules out. A null excludedCenter excludes nothing.
    /// </remarks>
    public static void ForEachCellOutside<TVisitor>(Vector3Int center, int radius, MapBounds bounds, Vector3Int? excludedCenter, ref TVisitor visitor)
        where TVisitor : struct, ICellVisitor
    {
        if (radius < 0)
        {
            return;
        }

        var excludes = excludedCenter is { } excluded && excluded.Z == center.Z;
        var excludedX = excludedCenter?.X ?? 0;
        var excludedY = excludedCenter?.Y ?? 0;
        var firstY = System.Math.Max(center.Y - radius, bounds.MinY);
        var lastY = System.Math.Min(center.Y + radius, bounds.MaxY - 1);

        for (var cellY = firstY; cellY <= lastY; cellY++)
        {
            var rowDistance = System.Math.Abs(cellY - center.Y);
            var remainingRadius = radius - rowDistance;
            var firstX = System.Math.Max(center.X - remainingRadius, bounds.MinX);
            var lastX = System.Math.Min(center.X + remainingRadius, bounds.MaxX - 1);

            var excludedRemainingRadius = excludes ? radius - System.Math.Abs(cellY - excludedY) : -1;
            if (excludedRemainingRadius < 0)
            {
                VisitRow(center, cellY, rowDistance, firstX, lastX, ref visitor);
                continue;
            }

            VisitRow(center, cellY, rowDistance, firstX, System.Math.Min(lastX, excludedX - excludedRemainingRadius - 1), ref visitor);
            VisitRow(center, cellY, rowDistance, System.Math.Max(firstX, excludedX + excludedRemainingRadius + 1), lastX, ref visitor);
        }
    }

    private static void VisitRow<TVisitor>(Vector3Int center, int cellY, int rowDistance, int firstX, int lastX, ref TVisitor visitor)
        where TVisitor : struct, ICellVisitor
    {
        for (var cellX = firstX; cellX <= lastX; cellX++)
        {
            visitor.Visit(new Vector3Int(cellX, cellY, center.Z), System.Math.Abs(cellX - center.X) + rowDistance);
        }
    }
}
