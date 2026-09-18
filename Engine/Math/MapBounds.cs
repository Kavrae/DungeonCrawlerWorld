namespace Engine.Math;

/// <summary>The rectangle of tiles a map covers, on every one of its Depth layers.</summary>
/// <remarks>MinX and MinY are inclusive, MaxX and MaxY exclusive. Either minimum can be negative: world coordinates are not anchored at 0.</remarks>
public readonly record struct MapBounds(int MinX, int MinY, int MaxX, int MaxY, int Depth)
{
    /// <summary>Bounds starting at (0, 0) with size's extent.</summary>
    public static MapBounds FromSize(Vector3Int size) => new(0, 0, size.X, size.Y, size.Z);

    public int Width => MaxX - MinX;

    public int Height => MaxY - MinY;

    public bool Contains(int x, int y) => (uint)(x - MinX) < (uint)Width && (uint)(y - MinY) < (uint)Height;

    public bool Contains(Vector3Int position) => Contains(position.X, position.Y) && (uint)position.Z < (uint)Depth;
}
