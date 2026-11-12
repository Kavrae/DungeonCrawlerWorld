using System.Runtime.InteropServices;
using Engine.Math;

namespace Engine.Tiles;

/// <summary>A reusable yes/no per tile over one rectangle of a layer: tiles found visible, or tiles already kept, held densely instead of in a set.</summary>
/// <remarks>A tile outside the rectangle is never marked. Release it when done, so a buffer grown for one very large area isn't kept.</remarks>
internal sealed class TileMarkGrid
{
    private bool[] _cells = [];
    private int _minX;
    private int _minY;
    private int _width;
    private int _height;

    /// <summary>Clears the grid and makes it cover width x height tiles from (minX, minY).</summary>
    public TileMarkGrid Reset(int minX, int minY, int width, int height)
    {
        _minX = minX;
        _minY = minY;
        _width = width;
        _height = height;
        var length = width * height;
        if (_cells.Length < length)
        {
            _cells = new bool[length];
        }
        else
        {
            Array.Clear(_cells, 0, length);
        }

        return this;
    }

    /// <summary>Clears the grid and makes it cover the smallest rectangle holding every tile of tiles from firstIndex on.</summary>
    public TileMarkGrid ResetAround(List<Vector3Int> tiles, int firstIndex)
    {
        if (firstIndex >= tiles.Count)
        {
            return Reset(0, 0, 0, 0);
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var tile in CollectionsMarshal.AsSpan(tiles)[firstIndex..])
        {
            minX = System.Math.Min(minX, tile.X);
            minY = System.Math.Min(minY, tile.Y);
            maxX = System.Math.Max(maxX, tile.X);
            maxY = System.Math.Max(maxY, tile.Y);
        }

        return Reset(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Drops a buffer larger than ScratchRetention.KeptCellCount.</summary>
    public void Release()
    {
        _width = 0;
        _height = 0;
        if (_cells.Length > ScratchRetention.KeptCellCount)
        {
            _cells = [];
        }
    }

    public void Add(Vector3Int tile)
    {
        var column = tile.X - _minX;
        var row = tile.Y - _minY;
        if ((uint)column < (uint)_width && (uint)row < (uint)_height)
        {
            _cells[row * _width + column] = true;
        }
    }

    /// <summary>Marks tile, returning whether it was unmarked; false for a tile outside the rectangle.</summary>
    public bool TryAdd(Vector3Int tile)
    {
        var column = tile.X - _minX;
        var row = tile.Y - _minY;
        if ((uint)column >= (uint)_width || (uint)row >= (uint)_height)
        {
            return false;
        }

        ref var cell = ref _cells[row * _width + column];
        if (cell)
        {
            return false;
        }

        cell = true;
        return true;
    }

    public bool Contains(Vector3Int tile)
    {
        var column = tile.X - _minX;
        var row = tile.Y - _minY;
        return (uint)column < (uint)_width && (uint)row < (uint)_height && _cells[row * _width + column];
    }

    /// <summary>Adds what LineOfSight.ComputeVisible finds into a TileMarkGrid.</summary>
    public readonly struct Sink(TileMarkGrid grid) : LineOfSight.IVisibleTileSink
    {
        public void Add(Vector3Int tile) => grid.Add(tile);
    }
}
