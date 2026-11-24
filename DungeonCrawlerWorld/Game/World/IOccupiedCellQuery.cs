namespace Game.World;

/// <summary>Reads the occupied cells of a rectangle on one MapLayer.</summary>
public interface IOccupiedCellQuery
{
    /// <summary>A scan of the occupied cells in the rectangle, Y ascending and then X ascending.</summary>
    /// <remarks>Cells off the map are skipped. Nothing may change occupants while the scan is open.</remarks>
    OccupiedCellScan GetOccupiedCellsIn(int minX, int minY, int maxXExclusive, int maxYExclusive, int mapLayer);
}
