using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Pure geometry backing TargetShapePreviewElement -- turns a TargetingSpec into relative tile
/// offsets from the caster (a small, static preview, not a live map query) plus a shrink-to-fit
/// cell size. Calls TargetShapeResolver.Resolve (Engine/Math/TargetShapeResolver.cs) directly
/// with the real Shape, Anchor and ranges -- not MapViewState.TargetableTiles, which is the area the
/// shape can be aimed at or reach in any direction (TargetShapeResolver.ResolveReachable), not one
/// aim's footprint.
/// </summary>
public static class TargetShapePreviewGeometry
{
    /// <summary>Comfortably larger than any realistic Range/AreaSize in every direction from the origin, so Resolve's clipping to the map's bounds never trims a preview.</summary>
    private const int SentinelMapExtent = 5000;
    private static readonly MapBounds SentinelBounds = new(-SentinelMapExtent, -SentinelMapExtent, SentinelMapExtent, SentinelMapExtent, 10);
    private static readonly Vector3Int SentinelOrigin = new(0, 0, 0);
    private static readonly Vector2Byte SingleTileFootprint = new(1, 1);

    private const float MinCellSize = 4f;
    private const float MaxCellSize = 20f;

    /// <summary>
    /// Relative (X, Y) tile offsets from the caster -- (0,0) is the caster's own tile, whether or
    /// not it's actually among the results (see TargetShape's own per-shape doc comments --
    /// Self (alone or combined, e.g. Adjacent | Self) includes it, everything else doesn't). Directional shapes (Line, Cone, Wall, Sweep), and anything with a minimum range, have no real cursor
    /// in a static preview, so they are aimed at maximum range in one fixed "north" direction (negative Y) --
    /// a display convention, not a claim about real facing.
    /// </summary>
    public static List<Point> ComputeOffsets(TargetingSpec spec)
    {
        var aimedTile = TargetShapeResolver.IsDirectional(spec.Shape) || spec.MinimumRange > 0
            ? SentinelOrigin + new Vector3Int(0, -System.Math.Max(1, spec.AimRanges.MaximumRange), 0)
            : SentinelOrigin;

        var results = new List<Vector3Int>();
        TargetShapeResolver.Resolve(spec.Shape, spec.Anchor, SentinelOrigin, SingleTileFootprint, aimedTile, spec.Ranges, SentinelBounds, results);

        var offsets = new List<Point>(results.Count);
        foreach (var tile in results)
        {
            offsets.Add(new Point(tile.X - SentinelOrigin.X, tile.Y - SentinelOrigin.Y));
        }

        return offsets;
    }

    /// <summary>
    /// Shrinks the per-cell pixel size so a shape's full bounding box (columns x rows) fits
    /// within maxWidth x maxHeightBudget -- clamped to [MinCellSize, MaxCellSize] so a huge AOE
    /// shrinks instead of consuming the window, and a tiny shape doesn't render as one giant tile.
    /// </summary>
    public static float ComputeCellSize(int columns, int rows, float maxWidth, float maxHeightBudget)
    {
        var fitWidth = maxWidth / System.Math.Max(1, columns);
        var fitHeight = maxHeightBudget / System.Math.Max(1, rows);
        return System.Math.Clamp(System.Math.Min(fitWidth, fitHeight), MinCellSize, MaxCellSize);
    }

    /// <summary>The grid's own bounding box, in the same relative-offset space ComputeOffsets returns -- always includes (0,0), the caster's own cell, even for a shape that doesn't target it (Line/Cone), so the player marker always has a cell to sit in.</summary>
    public static (int MinX, int MinY, int Columns, int Rows) ComputeBounds(IReadOnlyList<Point> offsets)
    {
        var minX = 0;
        var maxX = 0;
        var minY = 0;
        var maxY = 0;

        foreach (var offset in offsets)
        {
            minX = Math.Min(minX, offset.X);
            maxX = Math.Max(maxX, offset.X);
            minY = Math.Min(minY, offset.Y);
            maxY = Math.Max(maxY, offset.Y);
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
