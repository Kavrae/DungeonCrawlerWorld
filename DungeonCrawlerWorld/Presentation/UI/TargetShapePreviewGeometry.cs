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
    /// Self (alone or combined, e.g. Adjacent | Self) includes it, everything else doesn't). A static preview has no
    /// cursor, so a shape that depends on its aim (any but one laid around the caster facing no direction) is aimed
    /// at maximum range in one fixed "north" direction (negative Y) -- a display convention, not a claim about real
    /// facing -- so it sits at the far edge of its reach (ComputeReach).
    /// </summary>
    public static List<Point> ComputeOffsets(TargetingSpec spec)
    {
        spec = FitToPreview(spec);
        var aimedTile = TargetShapeResolver.IsAimIndependent(spec.Shape, spec.Anchor)
            ? SentinelOrigin
            : SentinelOrigin + new Vector3Int(0, -System.Math.Max(1, spec.AimRanges.MaximumRange), 0);

        var results = new List<Vector3Int>();
        TargetShapeResolver.Resolve(spec.Shape, spec.Anchor, SentinelOrigin, SingleTileFootprint, aimedTile, spec.Ranges, SentinelBounds, results);
        return ToOffsets(results);
    }

    /// <summary>Relative offsets of every tile spec can be aimed at or reach from the caster, whatever the aim (TargetShapeResolver.ResolveReachable over open ground): its range band, or a shape laid around the caster its own tiles.</summary>
    public static List<Point> ComputeReach(TargetingSpec spec)
    {
        spec = FitToPreview(spec);
        var results = new List<Vector3Int>();
        TargetShapeResolver.ResolveReachable(spec.Shape, spec.Anchor, SentinelOrigin, SingleTileFootprint, spec.Ranges, SentinelBounds, results, path: spec.Path, pathWalls: spec.PathWalls);
        results.RemoveAll(static tile => System.Math.Abs(tile.X - SentinelOrigin.X) > MaximumPreviewRadius || System.Math.Abs(tile.Y - SentinelOrigin.Y) > MaximumPreviewRadius);
        return ToOffsets(results);
    }

    /// <summary>How far from the caster a preview draws, in tiles: 2 x 11 + 1 = 23 cells across, which fit the details windows' 96 px grid cap at the 4 px minimum cell.</summary>
    public const int MaximumPreviewRadius = 11;

    /// <summary>spec with its distances shortened so its aim and shape fit MaximumPreviewRadius: the shape's size first kept whole up to the radius, then the range shortened to leave room for it. The preview shows the shape; the range is in its caption.</summary>
    public static TargetingSpec FitToPreview(TargetingSpec spec)
    {
        if (spec.MaximumRange + spec.AreaSize <= MaximumPreviewRadius)
        {
            return spec;
        }

        var areaSize = System.Math.Min(spec.AreaSize, MaximumPreviewRadius);
        var maximumRange = System.Math.Min(spec.MaximumRange, MaximumPreviewRadius - areaSize);
        return spec with
        {
            AreaSize = areaSize,
            MaximumRange = maximumRange,
            MinimumRange = System.Math.Min(spec.MinimumRange, maximumRange),
        };
    }

    private static List<Point> ToOffsets(List<Vector3Int> tiles)
    {
        var offsets = new List<Point>(tiles.Count);
        foreach (var tile in tiles)
        {
            offsets.Add(new Point(tile.X - SentinelOrigin.X, tile.Y - SentinelOrigin.Y));
        }

        return offsets;
    }

    /// <summary>The offsets an aura of size reaches around its source: every tile within that Manhattan distance, a diamond (AuraField.ReachOf's rule), the source's own tile included.</summary>
    public static List<Point> ComputeDiamondOffsets(int size)
    {
        var collector = new DiamondCollector(new List<Point>());
        ManhattanDiamond.ForEachCell(SentinelOrigin, size, SentinelBounds, ref collector);
        return collector.Offsets;
    }

    private readonly struct DiamondCollector(List<Point> offsets) : ManhattanDiamond.ICellVisitor
    {
        public List<Point> Offsets => offsets;

        public void Visit(Vector3Int cellPosition, int distance) => offsets.Add(new Point(cellPosition.X - SentinelOrigin.X, cellPosition.Y - SentinelOrigin.Y));
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
        return MathF.Floor(System.Math.Clamp(System.Math.Min(fitWidth, fitHeight), MinCellSize, MaxCellSize));
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
