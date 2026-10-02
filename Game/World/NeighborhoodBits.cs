using Engine.Math;

namespace Game.World;

/// <summary>One bit per cell, split by neighborhood: a dense yes/no for a question asked far more often than its answer changes.</summary>
/// <remarks>
/// A bitmap per neighborhood that has any bit set (1024 x 1024 x depth bits, about 131 KB a layer),
/// dropped again when its last bit clears. Reading a bit is an array read, where the sparse
/// NeighborhoodCells is a hash lookup -- which is what this is for: ruling a cell out before paying
/// for the lookup. The last neighborhood read is cached, as there.
/// </remarks>
public sealed class NeighborhoodBits(int depth)
{
    private const int CellMask = Neighborhoods.SizeTiles - 1;
    private const int BitsPerWord = 64;

    private readonly int _wordsPerNeighborhood = (depth << (2 * Neighborhoods.SizeShift)) / BitsPerWord;
    private readonly Dictionary<(int CellX, int CellY), Neighborhood> _byNeighborhood = [];

    private (int CellX, int CellY) _lastNeighborhoodCell = (int.MinValue, int.MinValue);
    private Neighborhood? _lastNeighborhood;

    private sealed class Neighborhood(int wordCount)
    {
        public ulong[] Words { get; } = new ulong[wordCount];

        public int SetBitCount { get; set; }
    }

    public bool IsSet(Vector3Int position)
    {
        if (NeighborhoodOf(position, create: false) is not { } neighborhood)
        {
            return false;
        }

        var bitIndex = BitIndex(position);
        return (neighborhood.Words[bitIndex / BitsPerWord] & (1UL << (bitIndex % BitsPerWord))) != 0;
    }

    public void Set(Vector3Int position)
    {
        var neighborhood = NeighborhoodOf(position, create: true)!;
        var bitIndex = BitIndex(position);
        ref var word = ref neighborhood.Words[bitIndex / BitsPerWord];
        var bit = 1UL << (bitIndex % BitsPerWord);
        if ((word & bit) == 0)
        {
            word |= bit;
            neighborhood.SetBitCount++;
        }
    }

    public void Clear(Vector3Int position)
    {
        if (NeighborhoodOf(position, create: false) is not { } neighborhood)
        {
            return;
        }

        var bitIndex = BitIndex(position);
        ref var word = ref neighborhood.Words[bitIndex / BitsPerWord];
        var bit = 1UL << (bitIndex % BitsPerWord);
        if ((word & bit) == 0)
        {
            return;
        }

        word &= ~bit;
        neighborhood.SetBitCount--;
        if (neighborhood.SetBitCount == 0)
        {
            _byNeighborhood.Remove((Neighborhoods.CellOf(position.X), Neighborhoods.CellOf(position.Y)));
            _lastNeighborhoodCell = (int.MinValue, int.MinValue);
            _lastNeighborhood = null;
        }
    }

    private Neighborhood? NeighborhoodOf(Vector3Int position, bool create)
    {
        var neighborhoodCell = (Neighborhoods.CellOf(position.X), Neighborhoods.CellOf(position.Y));
        if (neighborhoodCell == _lastNeighborhoodCell)
        {
            return _lastNeighborhood;
        }

        if (!_byNeighborhood.TryGetValue(neighborhoodCell, out var neighborhood))
        {
            if (!create)
            {
                return null;
            }

            neighborhood = new Neighborhood(_wordsPerNeighborhood);
            _byNeighborhood.Add(neighborhoodCell, neighborhood);
        }

        _lastNeighborhoodCell = neighborhoodCell;
        _lastNeighborhood = neighborhood;
        return neighborhood;
    }

    private static int BitIndex(Vector3Int position) =>
        (position.X & CellMask) | ((position.Y & CellMask) << Neighborhoods.SizeShift) | (position.Z << (2 * Neighborhoods.SizeShift));
}
