using Engine.Math;

namespace Game.World;

/// <summary>A sparse per-cell value store split by neighborhood, for grids that only hold entries near something -- an aura's reach, a glow.</summary>
/// <remarks>
/// One dictionary per neighborhood, keyed by the cell's offset within it, so a neighborhood's
/// entries can be dropped in one step when it unloads without
/// touching any other. The last neighborhood looked up is cached: scatters and radius scans visit
/// runs of cells in the same neighborhood, so most calls skip the outer lookup.
/// </remarks>
public sealed class NeighborhoodCells<TValue>
{
    private const int CellMask = Neighborhoods.SizeTiles - 1;

    private readonly Dictionary<(int CellX, int CellY), Dictionary<int, TValue>> _byNeighborhood = [];

    private (int CellX, int CellY) _lastNeighborhood = (int.MinValue, int.MinValue);
    private Dictionary<int, TValue>? _lastCells;

    /// <summary>The number of cells holding a value, across every neighborhood.</summary>
    public int Count { get; private set; }

    public bool TryGetValue(Vector3Int position, out TValue value)
    {
        if (CellsOf(position, create: false) is { } cells && cells.TryGetValue(LocalIndex(position), out value!))
        {
            return true;
        }

        value = default!;
        return false;
    }

    public TValue? GetValueOrDefault(Vector3Int position) => TryGetValue(position, out var value) ? value : default;

    public void Set(Vector3Int position, TValue value)
    {
        var cells = CellsOf(position, create: true)!;
        if (cells.TryAdd(LocalIndex(position), value))
        {
            Count++;
            return;
        }

        cells[LocalIndex(position)] = value;
    }

    public void Remove(Vector3Int position)
    {
        if (CellsOf(position, create: false) is { } cells && cells.Remove(LocalIndex(position)))
        {
            Count--;
        }
    }

    /// <summary>Drops every value in neighborhood (cellX, cellY).</summary>
    public void RemoveNeighborhood(int cellX, int cellY)
    {
        if (!_byNeighborhood.Remove((cellX, cellY), out var cells))
        {
            return;
        }

        Count -= cells.Count;
        if (_lastCells == cells)
        {
            _lastNeighborhood = (int.MinValue, int.MinValue);
            _lastCells = null;
        }
    }

    private Dictionary<int, TValue>? CellsOf(Vector3Int position, bool create)
    {
        var neighborhood = (Neighborhoods.CellOf(position.X), Neighborhoods.CellOf(position.Y));
        if (neighborhood == _lastNeighborhood)
        {
            return _lastCells;
        }

        if (!_byNeighborhood.TryGetValue(neighborhood, out var cells))
        {
            if (!create)
            {
                return null;
            }

            cells = [];
            _byNeighborhood.Add(neighborhood, cells);
        }

        _lastNeighborhood = neighborhood;
        _lastCells = cells;
        return cells;
    }

    private static int LocalIndex(Vector3Int position) =>
        (position.X & CellMask) | ((position.Y & CellMask) << Neighborhoods.SizeShift) | (position.Z << (2 * Neighborhoods.SizeShift));
}
