using System.Runtime.CompilerServices;

namespace Game.World;

/// <summary>One occupied column of an OccupiedColumnScan: a tile and the scanned MapLayers occupied there.</summary>
/// <param name="LayerMask">Bit N set when MapLayer N holds an occupant at (X, Y), only among the layers scanned.</param>
public readonly record struct OccupiedColumn(int X, int Y, byte LayerMask);

/// <summary>Walks the columns of a rectangle where any of a set of MapLayers is occupied, Y ascending and then X ascending.</summary>
/// <remarks>
/// Each row is read as one run of a neighborhood's column masks per neighborhood it crosses. It writes nothing, so a
/// default scan is empty and any number of scans can read the map at once.
/// </remarks>
public ref struct OccupiedColumnScan
{
    private readonly Map _map;

    private readonly int _minX;

    private readonly int _maxXExclusive;

    private readonly int _maxYExclusive;

    private readonly byte _layerMask;

    private readonly int _cellLayer;

    private MapNeighborhood? _segmentNeighborhood;

    private ReadOnlySpan<byte> _segmentColumnMasks;

    private int _segmentMinX;

    private int _segmentFirstCellIndex;

    private int _segmentOffset;

    private int _nextX;

    private int _y;

    private byte _currentLayerMask;

    /// <param name="cellLayer">The MapLayer CurrentCellIndex indexes, for a scan that reads one layer's cells.</param>
    internal OccupiedColumnScan(Map map, int minX, int minY, int maxXExclusive, int maxYExclusive, byte layerMask, int cellLayer)
    {
        _map = map;
        _minX = minX;
        _maxXExclusive = maxXExclusive;
        _maxYExclusive = maxYExclusive;
        _layerMask = layerMask;
        _cellLayer = cellLayer;
        _nextX = minX;
        _y = minY;
    }

    public readonly OccupiedColumn Current => new(CurrentX, _y, _currentLayerMask);

    internal readonly int CurrentX => _segmentMinX + _segmentOffset - 1;

    internal readonly int CurrentY => _y;

    internal readonly MapNeighborhood CurrentNeighborhood => _segmentNeighborhood!;

    /// <summary>The current column's cell on the cellLayer the scan was made with.</summary>
    internal readonly int CurrentCellIndex => _segmentFirstCellIndex + _segmentOffset - 1;

    public readonly OccupiedColumnScan GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (true)
        {
            var columnMasks = _segmentColumnMasks;
            var layerMask = _layerMask;
            for (var offset = _segmentOffset; offset < columnMasks.Length; offset++)
            {
                var occupiedLayers = columnMasks[offset] & layerMask;
                if (occupiedLayers == 0)
                {
                    continue;
                }

                _segmentOffset = offset + 1;
                _currentLayerMask = (byte)occupiedLayers;
                return true;
            }

            _segmentColumnMasks = default;
            if (!TryEnterNextSegment())
            {
                return false;
            }
        }
    }

    private bool TryEnterNextSegment()
    {
        while (_y < _maxYExclusive)
        {
            if (_nextX >= _maxXExclusive)
            {
                _nextX = _minX;
                _y++;
                continue;
            }

            var x = _nextX;
            var neighborhoodCell = Neighborhoods.CellOf(x);
            var segmentEnd = System.Math.Min(_maxXExclusive, Neighborhoods.OriginOf(neighborhoodCell + 1));
            _nextX = segmentEnd;

            if (_map.NeighborhoodAt(x, _y) is not { } neighborhood ||
                (x & Map.CellMask) >= neighborhood.Width ||
                (_y & Map.CellMask) >= neighborhood.Height)
            {
                continue;
            }

            var storedEnd = System.Math.Min(segmentEnd, Neighborhoods.OriginOf(neighborhoodCell) + neighborhood.Width);
            var firstColumnIndex = Map.ColumnIndex(neighborhood, x, _y);
            _segmentNeighborhood = neighborhood;
            _segmentMinX = x;
            _segmentFirstCellIndex = firstColumnIndex + _cellLayer * neighborhood.PlaneSize;
            _segmentColumnMasks = neighborhood.OccupiedLayerMaskByColumn.AsSpan(firstColumnIndex, storedEnd - x);
            _segmentOffset = 0;
            return true;
        }

        return false;
    }
}
