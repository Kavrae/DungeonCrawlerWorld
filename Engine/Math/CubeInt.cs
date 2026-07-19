namespace Engine.Math;

/// <summary>
/// An axis-aligned integer cube (position + size, in tiles), used for bounds-checking
/// multi-tile entities against the map.
/// </summary>
public struct CubeInt : IEquatable<CubeInt>
{
    public Vector3Int Position { get; set; }
    public Vector3Int Size { get; set; }

    public CubeInt(Vector3Int position)
    {
        Position = position;
        Size = new Vector3Int(1);
    }

    public CubeInt(Vector3Int position, Vector3Int size)
    {
        Position = position;
        Size = size;
    }

    public override bool Equals(object? obj) => obj is CubeInt other && Equals(other);

    public bool Equals(CubeInt other) => Position == other.Position && Size == other.Size;

    public static bool operator ==(CubeInt value1, CubeInt value2) => value1.Equals(value2);

    public static bool operator !=(CubeInt value1, CubeInt value2) => !value1.Equals(value2);

    public override int GetHashCode() => HashCode.Combine(Position, Size);

    public override string ToString() => $"{{Position:{Position} Size:{Size}}}";
}
