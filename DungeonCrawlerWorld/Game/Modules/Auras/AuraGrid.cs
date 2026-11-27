using Engine.Math;
using Engine.Tiles;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>
/// Precomputed per-cell, per-aura strongest power across the whole map, for every aura at once.
///
/// A source raises each cell of its reach to its value there (AuraFalloff), so a cell holds the strongest source's
/// value (PLAN-aura-scaling.md, Decision 1). A removed source's cells are found (CollectHeldCells), cleared, and raised
/// again from the sources still reaching them (RaiseCells) -- AuraField finds those, since it knows every source.
///
/// The values are AuraTotals' dense chunks. Beside them, one bit per cell says whether any aura
/// reaches it at all (IsCovered): most cells are in no aura, and most questions are asked of those,
/// so the common answer costs one read instead of one per aura.
///
/// Uses Manhattan distance (diamond-shaped falloff).
/// </summary>
/// <remarks>
/// A source's reach is clipped only to a bounded map's declared rectangle, which never changes. On
/// an unbounded map nothing is clipped: Bounds there follows the loaded neighborhoods. So a source at
/// the window's edge writes its reach into the unloaded neighbor's cells, where nothing reads it
/// until that neighbor loads -- with the reach already in place.
/// </remarks>
public sealed class AuraGrid
{
    private const int Unlimited = int.MaxValue;

    private readonly AuraTotals _totals;
    private readonly NeighborhoodBits _coveredCells;
    private readonly IMapQuery _map;

    /// <summary>One source's value at each distance from it, worked out once per source rather than once per cell; reused by every source.</summary>
    private int[] _valuesByDistance = [];

    /// <param name="map">Read for its depth, and for a bounded map's rectangle, past which no value is stored.</param>
    public AuraGrid(IMapQuery map)
    {
        _map = map;
        _totals = new AuraTotals(map.Bounds.Depth);
        _coveredCells = new NeighborhoodBits(map.Bounds.Depth);
    }

    /// <summary>The rectangle a source's reach is clipped to: a bounded map's own, and none at all on an unbounded one.</summary>
    private MapBounds ReachLimits =>
        _map.IsBounded ? _map.Bounds : new MapBounds(-Unlimited, -Unlimited, Unlimited, Unlimited, _map.Bounds.Depth);

    /// <summary>Chunks of values in use, across every aura.</summary>
    public int TotalsChunkCount => _totals.ChunkCount;

    /// <summary>Bytes the values hold, in use or kept for reuse.</summary>
    public long TotalsAllocatedBytes => _totals.AllocatedBytes;

    /// <summary>Every aura that has ever held a value in the grid.</summary>
    public IReadOnlyList<byte> AuraIdsWithTotals => _totals.AuraIdsWithTotals;

    /// <summary>Whether any aura reaches position. False means GetTotalPowerAt is zero there for every aura.</summary>
    public bool IsCovered(Vector3Int position) => _coveredCells.IsSet(position);

    /// <summary>The aura's strongest value at position: the strongest source's, by its falloff.</summary>
    public int GetTotalPowerAt(Vector3Int position, byte auraId) => _totals.Get(position, auraId);

    /// <summary>Raises every cell of a source's reach to its value there.</summary>
    /// <param name="excludedCenter">Skips the cells within size of it too: for a None source moved from there, whose value is unchanged where its old reach and new overlap.</param>
    public void RaiseSource(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId, Vector3Int? excludedCenter = null)
    {
        if (!FillValuesByDistance(power, size, falloff))
        {
            return;
        }

        var target = new RaiseTarget(this, auraId, _valuesByDistance);
        ManhattanDiamond.ForEachCellOutside(sourcePosition, size, ReachLimits, excludedCenter, ref target);
    }

    /// <summary>Adds to cells every cell of a source's reach whose value is the source's own there: the cells it may be holding, which change if it goes.</summary>
    /// <param name="excludedCenter">Skips the cells within size of it too: for a None source moved there, which still holds them at the same value.</param>
    public void CollectHeldCells(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId, List<Vector3Int> cells, Vector3Int? excludedCenter = null)
    {
        if (!FillValuesByDistance(power, size, falloff))
        {
            return;
        }

        var target = new HeldCellsTarget(_totals, auraId, _valuesByDistance, cells);
        ManhattanDiamond.ForEachCellOutside(sourcePosition, size, ReachLimits, excludedCenter, ref target);
    }

    /// <summary>Raises each of cells within a source's reach to the source's value there.</summary>
    public void RaiseCells(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId, List<Vector3Int> cells)
    {
        foreach (var cell in cells)
        {
            if (cell.Z != sourcePosition.Z)
            {
                continue;
            }

            var distance = Math.Abs(cell.X - sourcePosition.X) + Math.Abs(cell.Y - sourcePosition.Y);
            if (distance <= size)
            {
                RaiseCell(auraId, cell, falloff.ValueAt(power, size, distance));
            }
        }
    }

    /// <summary>Sets the aura's value at cell to zero, and the cell's coverage with it if no other aura reaches it.</summary>
    public void ClearCell(Vector3Int cell, byte auraId)
    {
        if (_totals.Clear(cell, auraId) == AuraTotals.CellChange.BecameZero && !_totals.AnyOtherAuraHas(cell, auraId))
        {
            _coveredCells.Clear(cell);
        }
    }

    /// <summary>Sets every aura's value to zero over area, every layer, and clears the area's coverage.</summary>
    public void ClearArea(MapBounds area)
    {
        _totals.ClearArea(area);
        for (var z = 0; z < area.Depth; z++)
        {
            for (var y = area.MinY; y < area.MaxY; y++)
            {
                for (var x = area.MinX; x < area.MaxX; x++)
                {
                    _coveredCells.Clear(new Vector3Int(x, y, z));
                }
            }
        }
    }

    /// <summary>Sets every aura's value to zero over a whole neighborhood, and drops its coverage.</summary>
    public void ClearNeighborhood(int cellX, int cellY)
    {
        var origin = new Vector3Int(Neighborhoods.OriginOf(cellX), Neighborhoods.OriginOf(cellY), 0);
        _totals.ClearArea(new MapBounds(origin.X, origin.Y, origin.X + Neighborhoods.SizeTiles, origin.Y + Neighborhoods.SizeTiles, _map.Bounds.Depth));
        _coveredCells.ClearNeighborhood(cellX, cellY);
    }

    /// <summary>Fills _valuesByDistance with a source's value at each distance; false when it reaches nothing.</summary>
    private bool FillValuesByDistance(int power, int size, AuraFalloff falloff)
    {
        if (size < 0 || power <= 0)
        {
            return false;
        }

        if (_valuesByDistance.Length <= size)
        {
            _valuesByDistance = new int[size + 1];
        }

        for (var distance = 0; distance <= size; distance++)
        {
            _valuesByDistance[distance] = falloff.ValueAt(power, size, distance);
        }

        return true;
    }

    private void RaiseCell(byte auraId, Vector3Int cellPosition, int value)
    {
        if (_totals.Raise(cellPosition, auraId, value) == AuraTotals.CellChange.BecameNonZero)
        {
            _coveredCells.Set(cellPosition);
        }
    }

    private readonly record struct RaiseTarget(AuraGrid Grid, byte AuraId, int[] ValuesByDistance) : ManhattanDiamond.ICellVisitor
    {
        public void Visit(Vector3Int cellPosition, int distance) =>
            Grid.RaiseCell(AuraId, cellPosition, ValuesByDistance[distance]);
    }

    private readonly record struct HeldCellsTarget(AuraTotals Totals, byte AuraId, int[] ValuesByDistance, List<Vector3Int> Cells) : ManhattanDiamond.ICellVisitor
    {
        public void Visit(Vector3Int cellPosition, int distance)
        {
            var value = ValuesByDistance[distance];
            if (value > 0 && Totals.Get(cellPosition, AuraId) == value)
            {
                Cells.Add(cellPosition);
            }
        }
    }
}
