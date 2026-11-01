using Engine.Math;
using Engine.Tiles;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>
/// Precomputed per-cell, per-aura total power across the whole map, for every aura at once.
///
/// Kept in sync incrementally as sources are added and removed (AddSource/RemoveSource), each
/// scattering or unscattering its value at each distance (AuraFalloff).
///
/// The totals are AuraTotals' dense chunks. Beside them, one bit per cell says whether any aura
/// reaches it at all (IsCovered): most cells are in no aura, and most questions are asked of those,
/// so the common answer costs one read instead of one per aura.
///
/// Uses Manhattan distance (diamond-shaped falloff).
/// </summary>
/// <remarks>
/// A source's reach is clipped only to a bounded map's declared rectangle, which never changes. On
/// an unbounded map nothing is clipped: Bounds there follows the loaded neighborhoods, and a source
/// added under one rectangle and removed under another would leave totals behind. So a source at
/// the window's edge writes its reach into the unloaded neighbor's cells, where nothing reads it
/// until that neighbor loads -- with the reach already in place.
/// </remarks>
public sealed class AuraGrid
{
    private const int Unlimited = int.MaxValue;

    private readonly AuraTotals _totals;
    private readonly NeighborhoodBits _coveredCells;
    private readonly IMapQuery _map;

    /// <summary>One splat's signed value at each distance from its source, worked out once per splat rather than once per cell; reused by every splat.</summary>
    private int[] _signedValuesByDistance = [];

    /// <param name="map">Read for its depth, and for a bounded map's rectangle, past which no contribution is stored.</param>
    public AuraGrid(IMapQuery map)
    {
        _map = map;
        _totals = new AuraTotals(map.Bounds.Depth);
        _coveredCells = new NeighborhoodBits(map.Bounds.Depth);
    }

    /// <summary>The rectangle a source's reach is clipped to: a bounded map's own, and none at all on an unbounded one.</summary>
    private MapBounds ReachLimits =>
        _map.IsBounded ? _map.Bounds : new MapBounds(-Unlimited, -Unlimited, Unlimited, Unlimited, _map.Bounds.Depth);

    /// <summary>Chunks of totals in use, across every aura.</summary>
    public int TotalsChunkCount => _totals.ChunkCount;

    /// <summary>Bytes the totals hold, in use or kept for reuse.</summary>
    public long TotalsAllocatedBytes => _totals.AllocatedBytes;

    /// <summary>Whether any aura reaches position. False means GetTotalPowerAt is zero there for every aura.</summary>
    public bool IsCovered(Vector3Int position) => _coveredCells.IsSet(position);

    public int GetTotalPowerAt(Vector3Int position, byte auraId) => _totals.Get(position, auraId);

    public void AddSource(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId) =>
        Splat(sourcePosition, power, size, falloff, auraId, sign: 1, excludedCenter: null);

    public void RemoveSource(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId) =>
        Splat(sourcePosition, power, size, falloff, auraId, sign: -1, excludedCenter: null);

    /// <summary>Moves one source's reach from previousPosition to currentPosition.</summary>
    /// <remarks>
    /// A None aura is the same value everywhere it reaches, so only the cells that changed sides are
    /// written: gained at the new position, lost at the old. A Linear one changes value under every
    /// cell, so both diamonds are written whole. Either way the new reach is added before the old is
    /// taken out, so a neighborhood the source never left keeps its storage throughout.
    /// </remarks>
    public void MoveSource(Vector3Int previousPosition, Vector3Int currentPosition, int power, int size, AuraFalloff falloff, byte auraId)
    {
        var writesChangedSidesOnly = falloff == AuraFalloff.None;
        Splat(currentPosition, power, size, falloff, auraId, sign: 1, excludedCenter: writesChangedSidesOnly ? previousPosition : null);
        Splat(previousPosition, power, size, falloff, auraId, sign: -1, excludedCenter: writesChangedSidesOnly ? currentPosition : null);
    }

    private void Splat(Vector3Int sourcePosition, int power, int size, AuraFalloff falloff, byte auraId, int sign, Vector3Int? excludedCenter)
    {
        if (size < 0)
        {
            return;
        }

        if (_signedValuesByDistance.Length <= size)
        {
            _signedValuesByDistance = new int[size + 1];
        }

        for (var distance = 0; distance <= size; distance++)
        {
            _signedValuesByDistance[distance] = sign * falloff.ValueAt(power, size, distance);
        }

        var target = new SplatTarget(this, auraId, _signedValuesByDistance);
        ManhattanDiamond.ForEachCellOutside(sourcePosition, size, ReachLimits, excludedCenter, ref target);
    }

    /// <summary>What one splat writes into: the aura, and the signed value it adds at each distance from the source.</summary>
    private readonly record struct SplatTarget(AuraGrid Grid, byte AuraId, int[] SignedValuesByDistance) : ManhattanDiamond.ICellVisitor
    {
        public void Visit(Vector3Int cellPosition, int distance) =>
            Grid.AddToCell(AuraId, cellPosition, SignedValuesByDistance[distance]);
    }

    private void AddToCell(byte auraId, Vector3Int cellPosition, int signedContribution)
    {
        switch (_totals.Add(cellPosition, auraId, signedContribution))
        {
            case AuraTotals.CellChange.BecameNonZero:
                _coveredCells.Set(cellPosition);
                break;
            case AuraTotals.CellChange.BecameZero when !_totals.AnyOtherAuraHas(cellPosition, auraId):
                _coveredCells.Clear(cellPosition);
                break;
        }
    }
}
