namespace Engine.Math;

/// <summary>
/// A 3D byte vector, used primarily for transform sizes (entity footprints rarely
/// exceed 255 tiles per axis).
/// </summary>
public struct Vector3Byte : IEquatable<Vector3Byte>
{
    public byte X;
    public byte Y;
    public byte Z;

    public Vector3Byte()
    {
        X = 0;
        Y = 0;
        Z = 0;
    }

    public Vector3Byte(byte value)
    {
        X = value;
        Y = value;
        Z = value;
    }

    public Vector3Byte(byte x, byte y, byte z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override bool Equals(object? obj) => obj is Vector3Byte other && Equals(other);

    public bool Equals(Vector3Byte other) => X == other.X && Y == other.Y && Z == other.Z;

    public static bool operator ==(Vector3Byte value1, Vector3Byte value2) => value1.Equals(value2);

    public static bool operator !=(Vector3Byte value1, Vector3Byte value2) => !value1.Equals(value2);

    /// <summary>Each component is clamped to [0, 255] rather than wrapping on overflow.</summary>
    public static Vector3Byte operator +(Vector3Byte value1, Vector3Byte value2) =>
        new(MathUtility.ClampByte(value1.X + value2.X), MathUtility.ClampByte(value1.Y + value2.Y), MathUtility.ClampByte(value1.Z + value2.Z));

    /// <summary>Each component is clamped to [0, 255] rather than wrapping on underflow.</summary>
    public static Vector3Byte operator -(Vector3Byte value1, Vector3Byte value2) =>
        new(MathUtility.ClampByte(value1.X - value2.X), MathUtility.ClampByte(value1.Y - value2.Y), MathUtility.ClampByte(value1.Z - value2.Z));

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() => $"{{X:{X} Y:{Y} Z:{Z}}}";
}
