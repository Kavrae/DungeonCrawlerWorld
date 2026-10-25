using System.Buffers.Binary;

namespace Engine.Diagnostics;

/// <summary>Reads one message at a time off a diagnostics link's stream, into a buffer it reuses.</summary>
/// <remarks>Blocking, so it is read from a thread of its own. A payload is valid only until the next read.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsLinkMessageReader(Stream stream)
{
    private readonly byte[] _header = new byte[DiagnosticsLinkProtocol.HeaderByteCount];
    private byte[] _payload = new byte[4096];

    /// <summary>Reads the next message; false when the stream ended cleanly between messages.</summary>
    /// <exception cref="InvalidDataException">The stream ended inside a message, or a length is out of range.</exception>
    public bool TryRead(out DiagnosticsLinkMessageType type, out ReadOnlyMemory<byte> payload)
    {
        type = default;
        payload = default;
        if (!TryFill(_header))
        {
            return false;
        }

        var payloadByteCount = BinaryPrimitives.ReadInt32LittleEndian(_header);
        if (payloadByteCount < 0 || payloadByteCount > DiagnosticsLinkProtocol.MaximumPayloadByteCount)
        {
            throw new InvalidDataException($"A diagnostics link message claims {payloadByteCount} bytes.");
        }

        if (_payload.Length < payloadByteCount)
        {
            _payload = new byte[System.Math.Max(payloadByteCount, _payload.Length * 2)];
        }

        var payloadBuffer = _payload.AsMemory(0, payloadByteCount);
        if (payloadByteCount > 0 && !TryFill(payloadBuffer.Span))
        {
            throw new InvalidDataException("The diagnostics link ended inside a message.");
        }

        type = (DiagnosticsLinkMessageType)_header[4];
        payload = payloadBuffer;
        return true;
    }

    /// <summary>Fills destination; false when the stream ended before its first byte.</summary>
    private bool TryFill(Span<byte> destination)
    {
        var filled = 0;
        while (filled < destination.Length)
        {
            var read = stream.Read(destination[filled..]);
            if (read == 0)
            {
                if (filled == 0)
                {
                    return false;
                }

                throw new InvalidDataException("The diagnostics link ended inside a message.");
            }

            filled += read;
        }

        return true;
    }
}
