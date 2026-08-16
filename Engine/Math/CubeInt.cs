namespace Engine.Math;

/// <summary> An axis-aligned integer cube used for bounds-checking multi-tile entities against the map. </summary>
/// <cleanupVersion>1</cleanupVersion>
public struct CubeInt : IEquatable<CubeInt>
{
    /// <summary>The position of the cube.</summary>
    public Vector3Int Position;

    /// <summary>The size of the cube.</summary>
    public Vector3Int Size;

    /// <summary>Create a new cube at the specified position with a size of 1x1x1.</summary>
    /// <param name="position">The position of the cube.</param>
    public CubeInt(Vector3Int position)
    {
        Position = position;
        Size = new Vector3Int(1);
    }

    /// <summary>Create a new cube at the specified position with the specified size.</summary>
    /// <param name="position">The position of the cube.</param>
    /// <param name="size">The size of the cube.</param>
    public CubeInt(Vector3Int position, Vector3Int size)
    {
        Position = position;
        Size = size;
    }

    public override bool Equals(object? obj) => obj is CubeInt other && Equals(other);

    public readonly bool Equals(CubeInt other) => Position == other.Position && Size == other.Size;

    public static bool operator ==(CubeInt value1, CubeInt value2) => value1.Equals(value2);

    public static bool operator !=(CubeInt value1, CubeInt value2) => !value1.Equals(value2);

    public override readonly int GetHashCode() => HashCode.Combine(Position, Size);

    public override readonly string ToString() => $"{{Position:{Position} Size:{Size}}}";
}