using System.Globalization;

namespace SpriteManifestBuilder;

/// <summary>Where a placed sprite draws relative to its footprint, and whether it stays inside the reach box.</summary>
/// <remarks>
/// Tile coordinates with (0,0) at the top-left of the footprint's anchor tile (its bottom-left tile), y down. A wall's top
/// is drawn raisedRows above the wall's tile, on top of its face: its rows above count from the wall's tile, while "below"
/// still means below the tile it sits on. The reach check and its message mirror Game/Sprites/SpriteReach.cs exactly, so
/// a save this tool allows loads in the game; the clamp mirrors what the map applies when it scales a sprite up to a larger
/// footprint. Keep both in sync by hand.
/// </remarks>
public static class SpritePlacementRules
{
    public const int MaximumRowsAbove = 2;
    public const int MaximumColumnsBeside = 2;

    /// <summary>The reach box in tiles around the anchor tile: 2 rows above, 2 columns either side, nothing below.</summary>
    public static RectangleF ReachBox => RectangleF.FromLTRB(-MaximumColumnsBeside, -MaximumRowsAbove, 1 + MaximumColumnsBeside, 1);

    /// <summary>The footprint of a square entity of footprintTiles tiles, anchored at its bottom-left tile.</summary>
    public static RectangleF Footprint(int footprintTiles) => RectangleF.FromLTRB(0, 1 - footprintTiles, footprintTiles, 1);

    /// <summary>The game's load check at a 1x1 footprint: null when the cell is inside the reach box, otherwise the game's own refusal message.</summary>
    /// <param name="partName">"top" for a wall's top, null for the entry's own cells.</param>
    /// <param name="pixelsPerTile">Null for a cell stretched to its tile.</param>
    public static string? DescribeOutsideReachBox(string entryName, string? partName, Size sourceSize, int? pixelsPerTile, Point pivot, int raisedRows)
    {
        double rowsAbove, columnsLeft, columnsRight, rowsBelow;
        bool isWithinReachBox;

        if (pixelsPerTile is not { } tilePixels)
        {
            (rowsAbove, columnsLeft, columnsRight, rowsBelow) = (raisedRows, 0, 0, 0);
            isWithinReachBox = raisedRows <= MaximumRowsAbove;
        }
        else
        {
            var halfPixelsPerTile = 2 * tilePixels;
            var left = tilePixels - 2 * pivot.X;
            var right = left + 2 * sourceSize.Width;
            var ownTileBottom = halfPixelsPerTile - raisedRows * halfPixelsPerTile;
            var top = ownTileBottom - 2 * pivot.Y;
            var bottom = top + 2 * sourceSize.Height;

            isWithinReachBox =
                left >= -MaximumColumnsBeside * halfPixelsPerTile &&
                right <= (1 + MaximumColumnsBeside) * halfPixelsPerTile &&
                top >= -MaximumRowsAbove * halfPixelsPerTile &&
                bottom <= ownTileBottom;

            rowsAbove = Math.Max(0, -top) / (double)halfPixelsPerTile;
            columnsLeft = Math.Max(0, -left) / (double)halfPixelsPerTile;
            columnsRight = Math.Max(0, right - halfPixelsPerTile) / (double)halfPixelsPerTile;
            rowsBelow = Math.Max(0, bottom - ownTileBottom) / (double)halfPixelsPerTile;
        }

        if (isWithinReachBox)
        {
            return null;
        }

        var subject = partName is null ? $"Sprite manifest entry '{entryName}'" : $"Sprite manifest entry '{entryName}' ({partName}, on a {raisedRows}-row face)";
        return $"{subject} reaches {Tiles(rowsAbove)} rows above, {Tiles(columnsLeft)} columns left, " +
            $"{Tiles(columnsRight)} columns right and {Tiles(rowsBelow)} rows below its tile at a 1x1 footprint; " +
            $"at most {MaximumRowsAbove} rows above, {MaximumColumnsBeside} columns to either side and none below are allowed.";
    }

    /// <summary>How many rows a part covers at a 1x1 footprint: its own tile plus every row it reaches above, a part row counting whole. Mirrors SpriteManifestPart.HeightRows.</summary>
    public static int HeightRows(IEnumerable<ManifestCell?> cells, int? pixelsPerTile, Func<ManifestCell, Point> pivotFor)
    {
        if (pixelsPerTile is not { } tilePixels)
        {
            return 1;
        }

        var rowsAbove = 0d;
        foreach (var cell in cells.OfType<ManifestCell>())
        {
            rowsAbove = Math.Max(rowsAbove, Math.Max(0, pivotFor(cell).Y - tilePixels) / (double)tilePixels);
        }

        return 1 + (int)Math.Ceiling(rowsAbove);
    }

    /// <summary>
    /// Where the sprite draws on a square footprint, in tiles: stretched to the footprint with no pixelsPerTile, otherwise
    /// sized by it, scaled up with the footprint, its pivot on the bottom-centre of the tile it sits on (raisedRows above the
    /// footprint's bottom) -- and, on a footprint larger than 1x1, shrunk uniformly about that pivot until it fits the reach box.
    /// </summary>
    /// <remarks>
    /// At 1x1 it is never shrunk: an entry must already fit there (the game refuses one that doesn't at load), so the previews
    /// show a 1x1 breach at its true size instead of hiding it inside the box.
    /// </remarks>
    public static RectangleF DrawnTiles(Size sourceSize, int? pixelsPerTile, Point pivot, int footprintTiles, int raisedRows = 0)
    {
        var footprint = Footprint(footprintTiles);
        if (pixelsPerTile is not { } tilePixels)
        {
            return new RectangleF(footprint.X, footprint.Y - raisedRows, footprint.Width, footprint.Height);
        }

        var pivotX = footprint.Left + footprintTiles / 2f;
        var pivotY = footprint.Bottom - raisedRows;
        var scale = footprintTiles / (float)tilePixels;
        var extentLeft = pivot.X * scale;
        var extentRight = (sourceSize.Width - pivot.X) * scale;
        var extentAbove = pivot.Y * scale;
        var extentBelow = (sourceSize.Height - pivot.Y) * scale;

        var box = ReachBox;
        var shrink = 1f;
        if (footprintTiles > 1)
        {
            shrink = Fit(shrink, extentLeft, pivotX - box.Left);
            shrink = Fit(shrink, extentRight, box.Right - pivotX);
            shrink = Fit(shrink, extentAbove, pivotY - box.Top);
            shrink = Fit(shrink, extentBelow, 0);
        }

        return RectangleF.FromLTRB(
            pivotX - extentLeft * shrink,
            pivotY - extentAbove * shrink,
            pivotX + extentRight * shrink,
            pivotY + extentBelow * shrink);
    }

    private static float Fit(float shrink, float extent, float room) =>
        extent > 0 && extent > room ? Math.Min(shrink, Math.Max(0, room) / extent) : shrink;

    private static string Tiles(double tiles) => tiles.ToString("0.###", CultureInfo.InvariantCulture);
}
