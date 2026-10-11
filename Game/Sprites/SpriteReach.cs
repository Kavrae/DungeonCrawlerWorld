namespace Game.Sprites;

/// <summary>How far, in tiles, a placed sprite draws past a 1x1 footprint on each side.</summary>
/// <remarks>
/// Everything an entity or structure draws must stay inside the reach box: at most MaximumRowsAbove rows above its
/// anchor tile, MaximumColumnsBeside columns to either side, and nothing below its own tile. The map's row walk
/// overscans by exactly that much, so the box is a rule content follows, not a number that grows to fit content.
/// A wall's top is drawn raisedRows above the wall's tile (on top of its face), so its rows above count from the wall's
/// tile while "below" still means below the tile it sits on. Worked in units of half a source pixel so the box's edges
/// compare exactly. DevTools/SpriteManifestBuilder keeps a copy of this check and its message; keep the two in sync.
/// </remarks>
public readonly record struct SpriteReach(double RowsAbove, double ColumnsLeft, double ColumnsRight, double RowsBelow, int RaisedRows, bool IsWithinReachBox)
{
    public const int MaximumRowsAbove = 2;
    public const int MaximumColumnsBeside = 2;

    public static SpriteReach Of(int sourceWidth, int sourceHeight, SpritePlacement placement, int raisedRows = 0)
    {
        var halfPixelsPerTile = 2 * placement.PixelsPerTile;
        var left = placement.PixelsPerTile - 2 * placement.PivotX;
        var right = left + 2 * sourceWidth;
        var ownTileBottom = halfPixelsPerTile - raisedRows * halfPixelsPerTile;
        var top = ownTileBottom - 2 * placement.PivotY;
        var bottom = top + 2 * sourceHeight;

        var isWithinReachBox =
            left >= -MaximumColumnsBeside * halfPixelsPerTile &&
            right <= (1 + MaximumColumnsBeside) * halfPixelsPerTile &&
            top >= -MaximumRowsAbove * halfPixelsPerTile &&
            bottom <= ownTileBottom;

        return new SpriteReach(
            System.Math.Max(0, -top) / (double)halfPixelsPerTile,
            System.Math.Max(0, -left) / (double)halfPixelsPerTile,
            System.Math.Max(0, right - halfPixelsPerTile) / (double)halfPixelsPerTile,
            System.Math.Max(0, bottom - ownTileBottom) / (double)halfPixelsPerTile,
            raisedRows,
            isWithinReachBox);
    }

    /// <summary>A sprite stretched to fill exactly its 1x1 tile, raisedRows above the anchor.</summary>
    public static SpriteReach OfStretched(int raisedRows = 0) =>
        new(raisedRows, 0, 0, 0, raisedRows, raisedRows <= MaximumRowsAbove);

    /// <param name="partName">Which part of the entry this is ("top"), or null for the entry's own cells.</param>
    public string DescribeOutsideReachBox(string entryName, string? partName = null)
    {
        var subject = partName is null ? $"Sprite manifest entry '{entryName}'" : $"Sprite manifest entry '{entryName}' ({partName}, on a {RaisedRows}-row face)";
        return $"{subject} reaches {Tiles(RowsAbove)} rows above, {Tiles(ColumnsLeft)} columns left, " +
            $"{Tiles(ColumnsRight)} columns right and {Tiles(RowsBelow)} rows below its tile at a 1x1 footprint; " +
            $"at most {MaximumRowsAbove} rows above, {MaximumColumnsBeside} columns to either side and none below are allowed.";
    }

    private static string Tiles(double tiles) => tiles.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
