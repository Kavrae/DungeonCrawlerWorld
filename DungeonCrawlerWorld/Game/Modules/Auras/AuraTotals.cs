using Engine.Math;
using Engine.Tiles;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Each cell's strongest power per aura, in dense chunks allocated where an aura reaches and freed when it stops.</summary>
/// <remarks>
/// <para>
/// Per aura id, per neighborhood, one slot per chunk of every layer; a chunk is a flat array of
/// values (ushort, a source's power), its count of non-zero cells kept beside the slots. Reading a cell is two array
/// reads after the neighborhood is found. An aura holds only the few neighborhoods around the window, so they
/// are a short list searched in order -- cheaper than hashing, for the exposure ticks that read
/// cells in no spatial order -- with the last one found checked first, as scatters visit runs of
/// cells in the same one.
/// </para>
/// <para>
/// A cell holds the strongest value any source gives it (PLAN-aura-scaling.md, Decision 1): Raise only ever raises it,
/// and a cell a removed source was holding is cleared and raised again from the sources still reaching it (AuraField).
/// A chunk is freed when its last cell returns to zero, and a neighborhood's slots when its last
/// chunk goes, so an unloaded neighborhood holds nothing. Freed ones are kept for reuse up to a small limit: a source
/// moving along a chunk or neighborhood edge frees and needs one again every step.
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

    private readonly int _depth = depth;
    private readonly int _chunkSlotsPerNeighborhood = depth << (2 * ChunksPerSideShift);
    private readonly AuraNeighborhoods?[] _neighborhoodsByAuraId = new AuraNeighborhoods?[byte.MaxValue + 1];
    private readonly List<byte> _auraIdsWithTotals = [];
    private readonly Stack<ushort[]> _spareChunks = [];
    private readonly Stack<Neighborhood> _spareNeighborhoods = [];

    /// <summary>What a write did to whether its cell holds anything for that aura.</summary>
    public enum CellChange : byte
    {
        Unchanged,
        BecameNonZero,
        BecameZero,
    }

    /// <summary>Chunks holding at least one non-zero value, across every aura.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Neighborhoods holding at least one chunk, counted once per aura.</summary>
    public int NeighborhoodCount { get; private set; }

    /// <summary>Every aura that has ever held a value here.</summary>
    public IReadOnlyList<byte> AuraIdsWithTotals => _auraIdsWithTotals;

    /// <summary>Bytes held by chunks and neighborhoods' chunk slots and counts, in use or kept for reuse.</summary>
    public long AllocatedBytes =>
        ((long)ChunkCount + _spareChunks.Count) * CellsPerChunk * sizeof(ushort)
        + ((long)NeighborhoodCount + _spareNeighborhoods.Count) * _chunkSlotsPerNeighborhood * (IntPtr.Size + sizeof(int));

    public int Get(Vector3Int position, byte auraId)
    {
        if (_neighborhoodsByAuraId[auraId]?.Find(position) is not { } neighborhood)
        {
            return 0;
        }

        return neighborhood.ChunkSlots[ChunkSlotOf(position)] is { } chunk ? chunk[CellIndexOf(position)] : 0;
    }

    /// <summary>Whether any aura other than exceptAuraId has a non-zero value at position.</summary>
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

    /// <summary>Raises the aura's value at position to value, if value is greater; a value of 0 or less changes nothing.</summary>
    public CellChange Raise(Vector3Int position, byte auraId, int value)
    {
        if (value <= 0 || value <= Get(position, auraId))
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
            chunkSlot = _spareChunks.Count > 0 ? _spareChunks.Pop() : new ushort[CellsPerChunk];
            neighborhood.ChunkCount++;
            ChunkCount++;
        }

        ref var cell = ref chunkSlot[CellIndexOf(position)];
        var wasZero = cell == 0;
        cell = (ushort)Math.Min(value, ushort.MaxValue);
        if (!wasZero)
        {
            return CellChange.Unchanged;
        }

        neighborhood.NonZeroCellCounts[chunkSlotIndex]++;
        return CellChange.BecameNonZero;
    }

    /// <summary>Sets the aura's value at position to zero.</summary>
    public CellChange Clear(Vector3Int position, byte auraId)
    {
        if (_neighborhoodsByAuraId[auraId] is not { } neighborhoods || neighborhoods.Find(position) is not { } neighborhood)
        {
            return CellChange.Unchanged;
        }

        var chunkSlotIndex = ChunkSlotOf(position);
        if (neighborhood.ChunkSlots[chunkSlotIndex] is not { } chunk || chunk[CellIndexOf(position)] == 0)
        {
            return CellChange.Unchanged;
        }

        chunk[CellIndexOf(position)] = 0;
        CountCleared(neighborhoods, neighborhood, chunkSlotIndex, clearedCells: 1);
        return CellChange.BecameZero;
    }

    /// <summary>Sets every aura's value to zero over area, every layer: for an area leaving the map.</summary>
    public void ClearArea(MapBounds area)
    {
        foreach (var auraId in _auraIdsWithTotals)
        {
            if (_neighborhoodsByAuraId[auraId] is not { } neighborhoods)
            {
                continue;
            }

            for (var chunkY = area.MinY >> ChunkShift; chunkY <= (area.MaxY - 1) >> ChunkShift; chunkY++)
            {
                for (var chunkX = area.MinX >> ChunkShift; chunkX <= (area.MaxX - 1) >> ChunkShift; chunkX++)
                {
                    var chunkOrigin = new Vector3Int(chunkX << ChunkShift, chunkY << ChunkShift, 0);
                    if (neighborhoods.Find(chunkOrigin) is not { } neighborhood)
                    {
                        continue;
                    }

                    for (var z = 0; z < Math.Min(area.Depth, _depth); z++)
                    {
                        ClearChunkArea(neighborhoods, neighborhood, chunkOrigin with { Z = z }, area);
                    }
                }
            }
        }
    }

    private void ClearChunkArea(AuraNeighborhoods neighborhoods, Neighborhood neighborhood, Vector3Int chunkOrigin, MapBounds area)
    {
        var chunkSlotIndex = ChunkSlotOf(chunkOrigin);
        if (neighborhood.ChunkSlots[chunkSlotIndex] is not { } chunk)
        {
            return;
        }

        var cleared = 0;
        for (var y = Math.Max(area.MinY, chunkOrigin.Y); y < Math.Min(area.MaxY, chunkOrigin.Y + ChunkSizeTiles); y++)
        {
            for (var x = Math.Max(area.MinX, chunkOrigin.X); x < Math.Min(area.MaxX, chunkOrigin.X + ChunkSizeTiles); x++)
            {
                ref var cell = ref chunk[CellIndexOf(new Vector3Int(x, y, 0))];
                if (cell != 0)
                {
                    cell = 0;
                    cleared++;
                }
            }
        }

        if (cleared > 0)
        {
            CountCleared(neighborhoods, neighborhood, chunkSlotIndex, cleared);
        }
    }

    /// <summary>Counts clearedCells cells of a chunk returned to zero, freeing the chunk with its last and the neighborhood with its last chunk.</summary>
    private void CountCleared(AuraNeighborhoods neighborhoods, Neighborhood neighborhood, int chunkSlotIndex, int clearedCells)
    {
        ref var nonZeroCellCount = ref neighborhood.NonZeroCellCounts[chunkSlotIndex];
        nonZeroCellCount -= clearedCells;
        if (nonZeroCellCount != 0)
        {
            return;
        }

        ref var chunkSlot = ref neighborhood.ChunkSlots[chunkSlotIndex];
        if (_spareChunks.Count < MaximumSpareChunks)
        {
            _spareChunks.Push(chunkSlot!);
        }

        chunkSlot = null;
        ChunkCount--;
        neighborhood.ChunkCount--;
        if (neighborhood.ChunkCount == 0)
        {
            RemoveNeighborhood(neighborhoods, neighborhood);
        }
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

        public ushort[]?[] ChunkSlots { get; } = new ushort[]?[chunkSlotCount];

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
