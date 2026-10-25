using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Diagnostics;

/// <summary>How messages are framed on the diagnostics link, in both directions: a little-endian int32 payload length, a DiagnosticsLinkMessageType byte, then the payload.</summary>
/// <remarks>Frame records are binary (DiagnosticsFrameRecord); every other payload is UTF-8 JSON written with JsonOptions.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsLinkProtocol
{
    /// <summary>Changes whenever a message's layout does; sent in the hello.</summary>
    public const int Version = 1;

    /// <summary>Bytes before each payload: its length and its type.</summary>
    public const int HeaderByteCount = 5;

    /// <summary>The largest payload a reader accepts; a larger length means the stream is not this protocol.</summary>
    public const int MaximumPayloadByteCount = 64 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Writes a message's header into destination, which holds at least HeaderByteCount bytes.</summary>
    public static void WriteHeader(Span<byte> destination, DiagnosticsLinkMessageType type, int payloadByteCount)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, payloadByteCount);
        destination[4] = (byte)type;
    }

    /// <summary>A whole message -- header and payload -- carrying value as JSON.</summary>
    public static byte[] JsonMessage<T>(DiagnosticsLinkMessageType type, T value) =>
        Message(type, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    /// <summary>A whole message -- header and payload -- carrying json, already serialized.</summary>
    public static byte[] JsonMessage(DiagnosticsLinkMessageType type, string json) =>
        Message(type, System.Text.Encoding.UTF8.GetBytes(json));

    /// <summary>A whole message with no payload.</summary>
    public static byte[] EmptyMessage(DiagnosticsLinkMessageType type) => Message(type, []);

    /// <summary>Reads a JSON payload as T.</summary>
    /// <exception cref="InvalidDataException">The payload is not T's JSON.</exception>
    public static T ReadJson<T>(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new InvalidDataException($"A {typeof(T).Name} payload was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"A {typeof(T).Name} payload is not valid: {exception.Message}", exception);
        }
    }

    /// <summary>Reads a JSON payload as text.</summary>
    public static string ReadText(ReadOnlySpan<byte> payload) => System.Text.Encoding.UTF8.GetString(payload);

    private static byte[] Message(DiagnosticsLinkMessageType type, byte[] payload)
    {
        var message = new byte[HeaderByteCount + payload.Length];
        WriteHeader(message, type, payload.Length);
        payload.CopyTo(message, HeaderByteCount);
        return message;
    }
}
