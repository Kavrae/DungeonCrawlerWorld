using Engine.Content;

namespace Game.Content;

/// <summary>Narrows an integer read from a file to its field's type, refusing one out of range rather than wrapping it.</summary>
public static class ContentNumbers
{
    public static short Int16(long value, string key) =>
        value is >= short.MinValue and <= short.MaxValue ? (short)value : throw OutOfRange(key, value, short.MinValue, short.MaxValue);

    public static ushort UInt16(long value, string key) =>
        value is >= ushort.MinValue and <= ushort.MaxValue ? (ushort)value : throw OutOfRange(key, value, ushort.MinValue, ushort.MaxValue);

    public static ushort? UInt16(long? value, string key) => value is { } present ? UInt16(present, key) : null;

    public static byte Byte(long value, string key) =>
        value is >= byte.MinValue and <= byte.MaxValue ? (byte)value : throw OutOfRange(key, value, byte.MinValue, byte.MaxValue);

    public static int Int32(long value, string key) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value : throw OutOfRange(key, value, int.MinValue, int.MaxValue);

    public static int? Int32(long? value, string key) => value is { } present ? Int32(present, key) : null;

    private static ContentFormatException OutOfRange(string key, long value, long minimum, long maximum) =>
        new($"\"{key}\" is {value}, outside {minimum} to {maximum}.");
}
