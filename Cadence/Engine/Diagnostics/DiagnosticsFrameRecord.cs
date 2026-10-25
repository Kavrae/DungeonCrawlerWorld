using System.Buffers.Binary;

namespace Engine.Diagnostics;

/// <summary>One frame record's fixed fields: what a frame cost and how the host was running; its gauge values follow it on the wire.</summary>
/// <remarks>
/// Binary, little-endian: frame (int64), update milliseconds, draw rate, update rate (doubles), flags (a byte, 1 = paused),
/// records dropped before this one (int32), gauge count (int32), then that many doubles in the session's gauge order. A
/// paused record repeats the last simulated frame's number and carries the host's rates, not a new frame.
/// </remarks>
/// <param name="DroppedBefore">Records the game's ring overwrote, unsent, since the record before this one.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct DiagnosticsFrameRecord(long Frame, double UpdateMilliseconds, double DrawRate, double UpdateRate, bool IsPaused, int DroppedBefore)
{
    /// <summary>Bytes before the gauge values.</summary>
    public const int FixedByteCount = 8 + 8 + 8 + 8 + 1 + 4 + 4;

    private const byte PausedFlag = 1;

    /// <summary>The bytes a record with gaugeCount gauge values takes.</summary>
    public static int ByteCountFor(int gaugeCount) => FixedByteCount + gaugeCount * sizeof(double);

    /// <summary>Writes record and gaugeValues into destination, which holds at least ByteCountFor(gaugeValues.Length) bytes, and returns how many it wrote.</summary>
    public static int Write(in DiagnosticsFrameRecord record, ReadOnlySpan<double> gaugeValues, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, record.Frame);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[8..], record.UpdateMilliseconds);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], record.DrawRate);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[24..], record.UpdateRate);
        destination[32] = record.IsPaused ? PausedFlag : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[33..], record.DroppedBefore);
        BinaryPrimitives.WriteInt32LittleEndian(destination[37..], gaugeValues.Length);
        for (var gaugeIndex = 0; gaugeIndex < gaugeValues.Length; gaugeIndex++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(destination[(FixedByteCount + gaugeIndex * sizeof(double))..], gaugeValues[gaugeIndex]);
        }

        return ByteCountFor(gaugeValues.Length);
    }

    /// <summary>Reads a record from source, copying its gauge values into gaugeValues, and returns how many values the record holds.</summary>
    /// <remarks>Values past gaugeValues' length are skipped.</remarks>
    /// <exception cref="InvalidDataException">source is shorter than its own counts say.</exception>
    public static int Read(ReadOnlySpan<byte> source, out DiagnosticsFrameRecord record, Span<double> gaugeValues)
    {
        if (source.Length < FixedByteCount)
        {
            throw new InvalidDataException($"A frame record needs {FixedByteCount} bytes; this one has {source.Length}.");
        }

        record = new DiagnosticsFrameRecord(
            BinaryPrimitives.ReadInt64LittleEndian(source),
            BinaryPrimitives.ReadDoubleLittleEndian(source[8..]),
            BinaryPrimitives.ReadDoubleLittleEndian(source[16..]),
            BinaryPrimitives.ReadDoubleLittleEndian(source[24..]),
            (source[32] & PausedFlag) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(source[33..]));

        var gaugeCount = BinaryPrimitives.ReadInt32LittleEndian(source[37..]);
        if (gaugeCount < 0 || source.Length < ByteCountFor(gaugeCount))
        {
            throw new InvalidDataException($"A frame record of {source.Length} bytes can't hold {gaugeCount} gauge values.");
        }

        var copiedCount = System.Math.Min(gaugeCount, gaugeValues.Length);
        for (var gaugeIndex = 0; gaugeIndex < copiedCount; gaugeIndex++)
        {
            gaugeValues[gaugeIndex] = BinaryPrimitives.ReadDoubleLittleEndian(source[(FixedByteCount + gaugeIndex * sizeof(double))..]);
        }

        return gaugeCount;
    }
}
