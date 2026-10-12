namespace SpriteFormat;

/// <summary>Where a placed sprite draws relative to its footprint, in tiles: sized by its pixels per tile, scaled with its footprint, its pivot on the footprint's bottom-centre, and kept inside the reach box.</summary>
/// <remarks>
/// Placement is authored for a 1x1 footprint (manifest load checks it fits the reach box there), and a larger footprint draws
/// the same art scaled per axis -- what stretching already does -- so a 2x2 or Huge goblin is the same picture, bigger, and a
/// 1x2 ghost keeps its shape. Scaling up can carry a sprite out of the reach box, which the map's row walk overscans by exactly,
/// so it is then shrunk uniformly about its pivot until it fits: never clipped, never drawn where the walk can't reach. A sprite
/// with no placement fills its footprint. At 1x1 nothing is shrunk: an entry must already fit there, so a tool's preview shows a
/// breach at its true size instead of hiding it inside the box.
/// </remarks>
public static class SpritePlacementTiles
{
    /// <summary>The reach box around a footprint footprintRows tall, anchored at its bottom-left tile, in tiles from the footprint's top-left corner.</summary>
    public static SpriteTileArea ReachBox(int footprintRows) =>
        new(-SpriteReach.MaximumColumnsBeside, footprintRows - 1 - SpriteReach.MaximumRowsAbove, 1 + SpriteReach.MaximumColumnsBeside, footprintRows);

    /// <summary>Where the sprite draws, in tiles from the footprint's top-left corner.</summary>
    /// <param name="placement">Null stretches it to fill the footprint.</param>
    /// <param name="raisedRows">How many rows above the footprint's bottom it sits: a wall's top, drawn on its face.</param>
    public static SpriteTileArea DrawnTiles(int footprintColumns, int footprintRows, int sourceWidth, int sourceHeight, SpritePlacement? placement, int raisedRows = 0)
    {
        if (placement is not { } placed)
        {
            return new SpriteTileArea(0, -raisedRows, footprintColumns, footprintRows - raisedRows);
        }

        var pivotX = footprintColumns / 2f;
        var pivotY = (float)(footprintRows - raisedRows);
        var scaleX = footprintColumns / (float)placed.PixelsPerTile;
        var scaleY = footprintRows / (float)placed.PixelsPerTile;

        var extentLeft = placed.PivotX * scaleX;
        var extentRight = (sourceWidth - placed.PivotX) * scaleX;
        var extentAbove = placed.PivotY * scaleY;
        var extentBelow = (sourceHeight - placed.PivotY) * scaleY;

        var shrink = 1f;
        if (footprintColumns > 1 || footprintRows > 1)
        {
            var reachBox = ReachBox(footprintRows);
            shrink = Fit(shrink, extentLeft, pivotX - reachBox.Left);
            shrink = Fit(shrink, extentRight, reachBox.Right - pivotX);
            shrink = Fit(shrink, extentAbove, pivotY - reachBox.Top);
            shrink = Fit(shrink, extentBelow, 0);
        }

        return new SpriteTileArea(pivotX - extentLeft * shrink, pivotY - extentAbove * shrink, pivotX + extentRight * shrink, pivotY + extentBelow * shrink);
    }

    private static float Fit(float shrink, float extent, float room) =>
        extent > 0 && extent > room ? Math.Min(shrink, Math.Max(0, room) / extent) : shrink;
}
