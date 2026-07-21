namespace Engine.Math;

/// <summary>
/// A 2D byte vector, used for entity footprint sizes (X/Y tile extent -- entity footprints
/// rarely exceed 255 tiles per axis, and never span more than one MapLayer, so there's no Z).
/// </summary>
public struct Vector2Byte : IEquatable<Vector2Byte>
{
    public byte X;
    public byte Y;

    public Vector2Byte()
    {
        X = 0;
        Y = 0;
    }

    public Vector2Byte(byte value)
    {
        X = value;
        Y = value;
    }

    public Vector2Byte(byte x, byte y)
    {
        X = x;
        Y = y;
    }

    public override bool Equals(object? obj) => obj is Vector2Byte other && Equals(other);

    public bool Equals(Vector2Byte other) => X == other.X && Y == other.Y;

    public static bool operator ==(Vector2Byte value1, Vector2Byte value2) => value1.Equals(value2);

    public static bool operator !=(Vector2Byte value1, Vector2Byte value2) => !value1.Equals(value2);

    /// <summary>Each component is clamped to [0, 255] rather than wrapping on overflow.</summary>
    public static Vector2Byte operator +(Vector2Byte value1, Vector2Byte value2) =>
        new(MathUtility.ClampByte(value1.X + value2.X), MathUtility.ClampByte(value1.Y + value2.Y));

    /// <summary>Each component is clamped to [0, 255] rather than wrapping on underflow.</summary>
    public static Vector2Byte operator -(Vector2Byte value1, Vector2Byte value2) =>
        new(MathUtility.ClampByte(value1.X - value2.X), MathUtility.ClampByte(value1.Y - value2.Y));

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"{{X:{X} Y:{Y}}}";
}
