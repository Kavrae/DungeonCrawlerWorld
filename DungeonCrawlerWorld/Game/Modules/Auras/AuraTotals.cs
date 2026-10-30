using Engine.Math;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Each cell's total power per aura, in dense chunks allocated where an aura reaches and freed when it stops.</summary>
/// <remarks>
/// <para>
/// Per aura id, per neighborhood, one slot per chunk of every layer; a chunk is a flat array of
/// totals, its count of non-zero cells kept beside the slots. Reading a cell is two array reads after
/// the neighborhood is found. An aura holds only the few neighborhoods around the window, so they
/// are a short list searched in order -- cheaper than hashing, for the exposure ticks that read
/// cells in no spatial order -- with the last one found checked first, as scatters visit runs of
/// cells in the same one.
/// </para>
/// <para>
/// A chunk is freed when its last cell returns to zero, and a neighborhood's slots when its last
/// chunk goes, so an unloaded neighborhood -- whose sources were all removed -- holds nothing. Freed
/// ones are kept for reuse up to a small limit: a source moving along a chunk or neighborhood edge
/// frees and needs one again every step.
/// </para>
/// </remarks>
public sealed class AuraTotals(int depth)
{
    private const int ChunkShift = 5;
    private const int ChunkSizeTiles = 1 << ChunkShift;
    private const int ChunkCellMask = ChunkSizeTiles - 1;
    private const int CellsPerChunk = ChunkSizeTiles * ChunkSizeTiles;
    private const int ChunksPerSideShift = Neighborhoods.SizeShift - ChunkShift;
    private const int ChunksPerSideMask = (1 << ChunksPerSideShift) - 1;
    private const int MaximumSpareChunks = 256;
    private const int MaximumSpareNeighborhoods = 8;

    private readonly int _chunkSlotsPerNeighborhood = depth << (2 * ChunksPerSideShift);
    private readonly AuraNeighborhoods?[] _neighborhoodsByAuraId = new AuraNeighborhoods?[byte.MaxValue + 1];
    private readonly List<byte> _auraIdsWithTotals = [];
    private readonly Stack<int[]> _spareChunks = [];
    private readonly Stack<Neighborhood> _spareNeighborhoods = [];

    /// <summary>What a write did to whether its cell holds anything for that aura.</summary>
    public enum CellChange : byte
    {
        Unchanged,
        BecameNonZero,
        BecameZero,
    }

    /// <summary>Chunks holding at least one non-zero total, across every aura.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Neighborhoods holding at least one chunk, counted once per aura.</summary>
    public int NeighborhoodCount { get; private set; }

    /// <summary>Bytes held by chunks and neighborhoods' chunk slots and counts, in use or kept for reuse.</summary>
    public long AllocatedBytes =>
        ((long)ChunkCount + _spareChunks.Count) * CellsPerChunk * sizeof(int)
        + ((long)NeighborhoodCount + _spareNeighborhoods.Count) * _chunkSlotsPerNeighborhood * (IntPtr.Size + sizeof(int));

    public int Get(Vector3Int position, byte auraId)
    {
        if (_neighborhoodsByAuraId[auraId]?.Find(position) is not { } neighborhood)
        {
            return 0;
        }

        return neighborhood.ChunkSlots[ChunkSlotOf(position)] is { } chunk ? chunk[CellIndexOf(position)] : 0;
    }

    /// <summary>Whether any aura other than exceptAuraId has a non-zero total at position.</summary>
    public bool AnyOtherAuraHas(Vector3Int position, byte exceptAuraId)
    {
        foreach (var auraId in _auraIdsWithTotals)
        {
            if (auraId != exceptAuraId && Get(position, auraId) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds signedContribution to the aura's total at position.</summary>
    public CellChange Add(Vector3Int position, byte auraId, int signedContribution)
    {
        if (signedContribution == 0)
        {
            return CellChange.Unchanged;
        }

        var neighborhoods = _neighborhoodsByAuraId[auraId];
        if (neighborhoods is null)
        {
            neighborhoods = new AuraNeighborhoods();
            _neighborhoodsByAuraId[auraId] = neighborhoods;
            _auraIdsWithTotals.Add(auraId);
        }

        var neighborhood = neighborhoods.Find(position) ?? AddNeighborhood(neighborhoods, position);
        var chunkSlotIndex = ChunkSlotOf(position);
        ref var chunkSlot = ref neighborhood.ChunkSlots[chunkSlotIndex];
        if (chunkSlot is null)
        {
            chunkSlot = _spareChunks.Count > 0 ? _spareChunks.Pop() : new int[CellsPerChunk];
            neighborhood.ChunkCount++;
            ChunkCount++;
        }

        var chunk = chunkSlot;
        ref var nonZeroCellCount = ref neighborhood.NonZeroCellCounts[chunkSlotIndex];
        ref var total = ref chunk[CellIndexOf(position)];
        var previousTotal = total;
        total = previousTotal + signedContribution;

        if (previousTotal == 0)
        {
            nonZeroCellCount++;
            return CellChange.BecameNonZero;
        }

        if (total != 0)
        {
            return CellChange.Unchanged;
        }

        nonZeroCellCount--;
        if (nonZeroCellCount == 0)
        {
            chunkSlot = null;
            ChunkCount--;
            if (_spareChunks.Count < MaximumSpareChunks)
            {
                _spareChunks.Push(chunk);
            }

            neighborhood.ChunkCount--;
            if (neighborhood.ChunkCount == 0)
            {
                RemoveNeighborhood(neighborhoods, neighborhood);
            }
        }

        return CellChange.BecameZero;
    }

    private Neighborhood AddNeighborhood(AuraNeighborhoods neighborhoods, Vector3Int position)
    {
        var neighborhood = _spareNeighborhoods.Count > 0 ? _spareNeighborhoods.Pop() : new Neighborhood(_chunkSlotsPerNeighborhood);
        neighborhood.NeighborhoodCellX = Neighborhoods.CellOf(position.X);
        neighborhood.NeighborhoodCellY = Neighborhoods.CellOf(position.Y);
        neighborhoods.Add(neighborhood);
        NeighborhoodCount++;
        return neighborhood;
    }

    private void RemoveNeighborhood(AuraNeighborhoods neighborhoods, Neighborhood neighborhood)
    {
        neighborhoods.Remove(neighborhood);
        NeighborhoodCount--;
        if (_spareNeighborhoods.Count < MaximumSpareNeighborhoods)
        {
            _spareNeighborhoods.Push(neighborhood);
        }
    }

    private static int ChunkSlotOf(Vector3Int position) =>
        ((position.X >> ChunkShift) & ChunksPerSideMask)
        | (((position.Y >> ChunkShift) & ChunksPerSideMask) << ChunksPerSideShift)
        | (position.Z << (2 * ChunksPerSideShift));

    private static int CellIndexOf(Vector3Int position) =>
        (position.X & ChunkCellMask) | ((position.Y & ChunkCellMask) << ChunkShift);

    /// <summary>One aura's chunks in one neighborhood, every layer, each slot's count of non-zero cells beside it. Its slots are all empty again before it is freed, and it is reused for whichever neighborhood needs one next.</summary>
    private sealed class Neighborhood(int chunkSlotCount)
    {
        public int NeighborhoodCellX { get; set; }

        public int NeighborhoodCellY { get; set; }

        public int[]?[] ChunkSlots { get; } = new int[]?[chunkSlotCount];

        public int[] NonZeroCellCounts { get; } = new int[chunkSlotCount];

        public int ChunkCount { get; set; }
    }

    /// <summary>One aura's neighborhoods, searched in order, with the last one found checked first.</summary>
    private sealed class AuraNeighborhoods
    {
        private readonly List<Neighborhood> _neighborhoods = [];
        private Neighborhood? _lastFound;

        public Neighborhood? Find(Vector3Int position)
        {
            var neighborhoodCellX = Neighborhoods.CellOf(position.X);
            var neighborhoodCellY = Neighborhoods.CellOf(position.Y);
            if (_lastFound is { } lastFound && lastFound.NeighborhoodCellX == neighborhoodCellX && lastFound.NeighborhoodCellY == neighborhoodCellY)
            {
                return lastFound;
            }

            foreach (var neighborhood in _neighborhoods)
            {
                if (neighborhood.NeighborhoodCellX == neighborhoodCellX && neighborhood.NeighborhoodCellY == neighborhoodCellY)
                {
                    _lastFound = neighborhood;
                    return neighborhood;
                }
            }

            return null;
        }

        public void Add(Neighborhood neighborhood)
        {
            _neighborhoods.Add(neighborhood);
            _lastFound = neighborhood;
        }

        public void Remove(Neighborhood neighborhood)
        {
            _neighborhoods.Remove(neighborhood);
            if (_lastFound == neighborhood)
            {
                _lastFound = null;
            }
        }
    }
}
