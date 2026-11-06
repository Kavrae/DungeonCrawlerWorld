using System.Text.Encodings.Web;
using System.Text.Json;

namespace Engine.Json;

/// <summary>How the engine writes JSON a person reads: text as it is (no escaping of non-ASCII or of characters only HTML cares about), compact or indented.</summary>
/// <remarks>For files written through Utf8JsonWriter: content tables and documents, a save's header. Reports written by JsonSerializer keep their own options.</remarks>
public static class JsonWriting
{
    /// <summary>Escapes only what JSON itself requires.</summary>
    public static JavaScriptEncoder Encoder { get; } = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>One line, no whitespace between tokens.</summary>
    public static JsonWriterOptions Compact { get; } = new() { Encoder = Encoder, Indented = false };

    /// <summary>The writer's own indented layout.</summary>
    public static JsonWriterOptions Indented { get; } = new() { Encoder = Encoder, Indented = true };
}
