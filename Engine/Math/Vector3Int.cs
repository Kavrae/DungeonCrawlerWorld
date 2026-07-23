namespace Engine.Math;

/// <summary>
/// A 3D integer vector, used for positioning entities within the game world grid
/// (XNA/FNA's Vector3 is float-only). Z is an int rather than an enum to avoid boxing
/// when used as a dictionary key or array index.
/// </summary>
public struct Vector3Int : IEquatable<Vector3Int>
{
    public int X;
    public int Y;
    public int Z;

    public Vector3Int()
    {
        X = 0;
        Y = 0;
        Z = 0;
    }

    public Vector3Int(int value)
    {
        X = value;
        Y = value;
        Z = value;
    }

    public Vector3Int(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override bool Equals(object? obj) => obj is Vector3Int other && Equals(other);

    public readonly bool Equals(Vector3Int other) => X == other.X && Y == other.Y && Z == other.Z;

    public static bool operator ==(Vector3Int value1, Vector3Int value2) => value1.Equals(value2);

    public static bool operator !=(Vector3Int value1, Vector3Int value2) => !value1.Equals(value2);

    public static Vector3Int operator +(Vector3Int value1, Vector3Int value2) =>
        new(value1.X + value2.X, value1.Y + value2.Y, value1.Z + value2.Z);

    public static Vector3Int operator -(Vector3Int value1, Vector3Int value2) =>
        new(value1.X - value2.X, value1.Y - value2.Y, value1.Z - value2.Z);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z}}}";
}