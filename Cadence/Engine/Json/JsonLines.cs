using System.Text.Json;

namespace Engine.Json;

/// <summary>Where one line of a JSON Lines file is: its 1-based number, its bytes' offset and length (without its line break, '\r' included), and whether a line break ends it.</summary>
public readonly record struct JsonLine(int Number, int Offset, int Length, bool IsComplete);

/// <summary>One line of a JSON Lines file, parsed: its document, or why it isn't JSON. The caller disposes the document.</summary>
public readonly record struct JsonLineResult(JsonLine Line, JsonDocument? Document, string? Problem);

/// <summary>Reads a JSON Lines file -- one JSON value per line -- by the rules every such file in the engine keeps.</summary>
/// <remarks>
/// Lines are numbered from 1 as the file holds them, blank lines included; a blank line (spaces, tabs, a '\r') holds
/// nothing and is skipped. A last line with no line break that won't parse was cut short (a crash mid-append) and is
/// left out, so the file reads as everything written before it; any other line that won't parse is reported. Works on
/// the file's UTF-8 bytes, so a line's offset and length name the bytes a later read takes it from.
/// </remarks>
public static class JsonLines
{
    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Every line of bytes that isn't blank, in order; a UTF-8 byte order mark at the start is no part of the first line.</summary>
    public static IEnumerable<JsonLine> Split(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith(Utf8ByteOrderMark) ? Utf8ByteOrderMark.Length : 0;
        var number = 0;
        while (start < bytes.Length)
        {
            number++;
            var end = Array.IndexOf(bytes, (byte)'\n', start);
            var isComplete = end >= 0;
            var lineEnd = isComplete ? end : bytes.Length;
            var length = lineEnd - start;
            if (length > 0 && bytes[lineEnd - 1] == (byte)'\r')
            {
                length--;
            }

            var line = new JsonLine(number, start, length, isComplete);
            start = isComplete ? end + 1 : bytes.Length;
            if (!IsBlank(bytes.AsSpan(line.Offset, line.Length)))
            {
                yield return line;
            }
        }
    }

    /// <summary>Every line of bytes that isn't blank, parsed, in order -- less a cut-short last line.</summary>
    public static IEnumerable<JsonLineResult> Read(byte[] bytes)
    {
        foreach (var line in Split(bytes))
        {
            if (TryParse(bytes, line, out var document, out var problem))
            {
                yield return new JsonLineResult(line, document, null);
            }
            else if (line.IsComplete)
            {
                yield return new JsonLineResult(line, null, problem);
            }
        }
    }

    /// <summary>The document line of bytes holds; false, with the parser's message, when it isn't JSON.</summary>
    public static bool TryParse(byte[] bytes, JsonLine line, out JsonDocument document, out string problem)
    {
        try
        {
            document = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, line.Offset, line.Length));
            problem = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            document = null!;
            problem = exception.Message;
            return false;
        }
    }

    private static bool IsBlank(ReadOnlySpan<byte> line)
    {
        foreach (var character in line)
        {
            if (character is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
            {
                return false;
            }
        }

        return true;
    }
}
