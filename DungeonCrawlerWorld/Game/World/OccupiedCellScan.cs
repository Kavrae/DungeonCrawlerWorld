using Engine.Math;

namespace Game.World;

/// <summary>Walks the occupied cells of a rectangle on one MapLayer, Y ascending and then X ascending.</summary>
/// <remarks>
/// An OccupiedColumnScan of that one layer, building an occupant span only for a cell whose bit is set. It writes
/// nothing, so a default scan is empty and any number of scans can read the map at once.
/// </remarks>
public ref struct OccupiedCellScan
{
    private OccupiedColumnScan _columns;

    private readonly int _mapLayer;

    private OccupiedCell _current;

    internal OccupiedCellScan(Map map, int minX, int minY, int maxXExclusive, int maxYExclusive, int mapLayer)
    {
        _columns = new OccupiedColumnScan(map, minX, minY, maxXExclusive, maxYExclusive, (byte)(1 << mapLayer), mapLayer);
        _mapLayer = mapLayer;
    }

    public readonly OccupiedCell Current => _current;

    public readonly OccupiedCellScan GetEnumerator() => this;

    public bool MoveNext()
    {
        if (!_columns.MoveNext())
        {
            return false;
        }

        _current = new OccupiedCell(new Vector3Int(_columns.CurrentX, _columns.CurrentY, _mapLayer), Map.OccupantsInCell(_columns.CurrentNeighborhood, _columns.CurrentCellIndex));
        return true;
    }
}
