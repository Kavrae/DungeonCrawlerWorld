namespace SpriteManifestBuilder;

/// <summary>Where a placed sprite draws relative to its footprint, in this tool's preview frame: SpritePlacementTiles, measured from the anchor tile.</summary>
/// <remarks>Tile coordinates with (0,0) at the top-left of the footprint's anchor tile (its bottom-left tile), y down, for square footprints.</remarks>
public static class SpritePlacementRules
{
    /// <summary>The reach box in tiles around the anchor tile: 2 rows above, 2 columns either side, nothing below.</summary>
    public static RectangleF ReachBox => FromAnchor(SpritePlacementTiles.ReachBox(footprintRows: 1), footprintTiles: 1);

    /// <summary>The footprint of a square entity of footprintTiles tiles, anchored at its bottom-left tile.</summary>
    public static RectangleF Footprint(int footprintTiles) => RectangleF.FromLTRB(0, 1 - footprintTiles, footprintTiles, 1);

    /// <summary>Where the sprite draws on a square footprint, in tiles: SpritePlacementTiles.DrawnTiles with the pivot given, raisedRows above the footprint's bottom.</summary>
    /// <param name="pixelsPerTile">Null stretches it to the footprint.</param>
    public static RectangleF DrawnTiles(Size sourceSize, int? pixelsPerTile, Point pivot, int footprintTiles, int raisedRows = 0)
    {
        SpritePlacement? placement = pixelsPerTile is { } tilePixels ? new SpritePlacement(tilePixels, pivot.X, pivot.Y) : null;
        var area = SpritePlacementTiles.DrawnTiles(footprintTiles, footprintTiles, sourceSize.Width, sourceSize.Height, placement, raisedRows);
        return FromAnchor(area, footprintTiles);
    }

    private static RectangleF FromAnchor(SpriteTileArea area, int footprintTiles) =>
        RectangleF.FromLTRB(area.Left, area.Top + 1 - footprintTiles, area.Right, area.Bottom + 1 - footprintTiles);
}
