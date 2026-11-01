using System.Collections;
using System.Runtime.InteropServices;
using Engine.Math;

namespace Engine.Tiles;

/// <summary>Tiles on one layer, each once, kept in the order they were given and asked about in constant time.</summary>
/// <remarks>
/// Membership is a dense yes/no over the tiles' own rectangle (TileMarkGrid), so Contains is an array read where a
/// HashSet would hash; a shape's or a reachable area's tiles are close together, so the rectangle stays small. Refilled
/// in place with SetTo, so it allocates only while its buffers grow.
/// </remarks>
public sealed class TileSet : IReadOnlyList<Vector3Int>
{
    private readonly List<Vector3Int> _tiles = [];
    private readonly TileMarkGrid _marks = new();
    private int _layer;

    public int Count => _tiles.Count;

    public Vector3Int this[int index] => _tiles[index];

    /// <summary>Replaces the contents with tiles, in their order, leaving out repeats.</summary>
    /// <exception cref="ArgumentException">The tiles are on more than one layer.</exception>
    public void SetTo(ReadOnlySpan<Vector3Int> tiles)
    {
        _tiles.Clear();
        if (tiles.IsEmpty)
        {
            _marks.Release();
            _marks.Reset(0, 0, 0, 0);
            return;
        }

        _layer = tiles[0].Z;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var tile in tiles)
        {
            if (tile.Z != _layer)
            {
                throw new ArgumentException($"A TileSet holds tiles of one layer; {tile} is not on layer {_layer}.", nameof(tiles));
            }

            minX = System.Math.Min(minX, tile.X);
            minY = System.Math.Min(minY, tile.Y);
            maxX = System.Math.Max(maxX, tile.X);
            maxY = System.Math.Max(maxY, tile.Y);
        }

        _marks.Reset(minX, minY, maxX - minX + 1, maxY - minY + 1);
        foreach (var tile in tiles)
        {
            if (_marks.TryAdd(tile))
            {
                _tiles.Add(tile);
            }
        }
    }

    /// <summary>Empties the set.</summary>
    public void Clear() => SetTo(ReadOnlySpan<Vector3Int>.Empty);

    public bool Contains(Vector3Int tile) => tile.Z == _layer && _marks.Contains(tile);

    public ReadOnlySpan<Vector3Int> AsSpan() => CollectionsMarshal.AsSpan(_tiles);

    /// <summary>Walks the tiles in order without allocating.</summary>
    public List<Vector3Int>.Enumerator GetEnumerator() => _tiles.GetEnumerator();

    IEnumerator<Vector3Int> IEnumerable<Vector3Int>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
