using Engine.Math;

namespace Engine.Tiles;

/// <summary>A reusable memo of another ISightBlockers over one rectangle of one layer, asking it about each tile at most once.</summary>
/// <remarks>
/// Several sweeps or rays over the same area (each tile of a large caster's footprint) share what one of them has asked. A
/// tile outside the rectangle, or on another layer, is passed straight through. Release it when done: it holds the walls
/// it was given -- in a game, the session's world -- until then.
/// </remarks>
internal sealed class SightBlockerCache : ISightBlockers
{
    private const byte Unknown = 0;
    private const byte Open = 1;
    private const byte Blocked = 2;

    /// <summary>The one cache the Tiles code shares, per thread; Around hands it out, and each user releases it before another takes it.</summary>
    [ThreadStatic]
    private static SightBlockerCache? _shared;

    private byte[] _cells = [];
    private ISightBlockers? _walls;

    /// <summary>The shared cache over walls, covering the footprint at anchor and margin + 1 tiles around it: everything a shape or path of that reach laid out there, and the rays to it, ask about.</summary>
    /// <remarks>A tile outside it is asked of walls directly, so the margin decides only what is remembered. The caller releases it.</remarks>
    public static SightBlockerCache Around(ISightBlockers walls, Vector3Int anchor, Vector2Byte anchorSize, int margin)
    {
        var extent = System.Math.Max(margin, 0) + 1;
        return (_shared ??= new SightBlockerCache())
            .Over(walls, anchor.X - extent, anchor.Y - extent, anchorSize.X + 2 * extent, anchorSize.Y + 2 * extent, anchor.Z);
    }
    private int _minX;
    private int _minY;
    private int _width;
    private int _height;
    private int _layer;

    /// <summary>Forgets everything and remembers walls over width x height tiles from (minX, minY) on layer.</summary>
    public SightBlockerCache Over(ISightBlockers walls, int minX, int minY, int width, int height, int layer)
    {
        _walls = walls;
        _minX = minX;
        _minY = minY;
        _width = width;
        _height = height;
        _layer = layer;
        var length = width * height;
        if (_cells.Length < length)
        {
            _cells = new byte[length];
        }
        else
        {
            Array.Clear(_cells, 0, length);
        }

        return this;
    }

    /// <summary>Lets go of the walls, and of a buffer larger than ScratchRetention.KeptCellCount, until the next Over.</summary>
    public void Release()
    {
        _walls = null;
        _width = 0;
        _height = 0;
        if (_cells.Length > ScratchRetention.KeptCellCount)
        {
            _cells = [];
        }
    }

    public int Version => _walls?.Version ?? 0;

    public bool BlocksSight(int x, int y, int z)
    {
        var walls = _walls ?? throw new InvalidOperationException("SightBlockerCache was used after Release.");
        var column = x - _minX;
        var row = y - _minY;
        if ((uint)column >= (uint)_width || (uint)row >= (uint)_height || z != _layer)
        {
            return walls.BlocksSight(x, y, z);
        }

        ref var cell = ref _cells[row * _width + column];
        if (cell == Unknown)
        {
            cell = walls.BlocksSight(x, y, z) ? Blocked : Open;
        }

        return cell == Blocked;
    }
}
