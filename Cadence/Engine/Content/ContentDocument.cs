using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Engine.Content;

/// <summary>Where each value of a content document starts in its file, by path: property names and list indices joined by '/' ("lay/Ground/3", "ent/0/x"); the record itself is "".</summary>
public sealed class ContentSourceMap
{
    private readonly Dictionary<string, (int Line, int Column)> _positionsByPath;

    internal ContentSourceMap(string file, Dictionary<string, (int Line, int Column)> positionsByPath)
    {
        File = file;
        _positionsByPath = positionsByPath;
    }

    public string File { get; }

    /// <summary>The 1-based line and column path's value starts at; false for a path the file doesn't hold.</summary>
    public bool TryGetPosition(string path, out int line, out int column)
    {
        if (_positionsByPath.TryGetValue(path, out var position))
        {
            (line, column) = position;
            return true;
        }

        line = 0;
        column = 0;
        return false;
    }

    /// <summary>The line path's value starts on, or the line of the nearest enclosing value the file holds.</summary>
    public int LineOf(string path)
    {
        var current = path;
        while (true)
        {
            if (_positionsByPath.TryGetValue(current, out var position))
            {
                return position.Line;
            }

            if (current.Length == 0)
            {
                return 1;
            }

            var separator = current.LastIndexOf('/');
            current = separator < 0 ? string.Empty : current[..separator];
        }
    }
}

/// <summary>A record read from a content document, with where each of its values sits in the file.</summary>
public sealed record ContentDocumentRecord<T>(Guid Id, T? Value, bool IsDisabled, Guid? Replacement, ContentSourceMap Source)
    where T : class;

/// <summary>One record of a table as a file of its own: one indented JSON object holding the format and table first, then the record's fields, with every list one element per line.</summary>
/// <remarks>
/// For records too large for one line of a JSON Lines table -- a map area's rows -- so that a person can read and write
/// one, and a diff shows the line that changed. The layout is canonical: an object that isn't inside a list has one
/// field per line, a list has one element per line, and anything inside a list is written compactly on its line. So a
/// list of strings (an area's rows) is one string per line, and a list of objects (its placements) one object per line.
/// The record carries the same "id", "off" and "rep" as a table's line.
/// </remarks>
public static class ContentDocument
{
    private const string Indent = "  ";

    private static readonly IReadOnlySet<string> EnvelopeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        ContentTable.FormatKey, ContentTable.TableKey, ContentTable.DisabledKey, ContentTable.ReplacementKey,
    };

    private static readonly JavaScriptEncoder NameEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Reads the record of path, which must be a document of table tableName; null, with a failure added, when it can't be read.</summary>
    public static ContentDocumentRecord<T>? Read<T>(string path, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class =>
        Read<T>(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path), tableName, types, references, failures);

    /// <summary>Reads the record of text, a whole document; fileName names it in failures and in the record's source map.</summary>
    public static ContentDocumentRecord<T>? Read<T>(string text, string fileName, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            failures.Add(new ContentFailure(fileName, (int)(exception.LineNumber ?? 0) + 1, null, $"Not valid JSON: {exception.Message}", (int)(exception.BytePositionInLine ?? 0) + 1));
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!ContentTable.TryReadHeader(root, tableName, out var headerProblem))
            {
                failures.Add(new ContentFailure(fileName, 1, null, headerProblem));
                return null;
            }

            var record = ContentTable.TryReadRecord<T>(root, fileName, 1, types, references, failures, EnvelopeKeys);
            return record is null
                ? null
                : new ContentDocumentRecord<T>(record.Id, record.Value, record.IsDisabled, record.Replacement, MapPositions(bytes, fileName));
        }
    }

    /// <summary>The whole document for record of table tableName, ending in '\n'.</summary>
    public static string Write<T>(string tableName, T record, ContentTypeRegistry types)
        where T : notnull
    {
        var compact = ContentWriter.WriteRecord(types, record, header: json =>
        {
            json.WriteNumber(ContentTable.FormatKey, ContentTable.FormatVersion);
            json.WriteString(ContentTable.TableKey, tableName);
        });

        using var document = JsonDocument.Parse(compact);
        var text = new StringBuilder();
        WriteExpanded(text, document.RootElement, depth: 0);
        return text.Append('\n').ToString();
    }

    private static void WriteExpanded(StringBuilder text, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteExpandedObject(text, element, depth);
                break;
            case JsonValueKind.Array:
                WriteExpandedArray(text, element, depth);
                break;
            default:
                text.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteExpandedObject(StringBuilder text, JsonElement element, int depth)
    {
        text.Append('{').Append('\n');
        var isFirst = true;
        foreach (var property in element.EnumerateObject())
        {
            if (!isFirst)
            {
                text.Append(',').Append('\n');
            }

            isFirst = false;
            AppendIndent(text, depth + 1);
            text.Append('"').Append(NameEncoder.Encode(property.Name)).Append("\": ");
            WriteExpanded(text, property.Value, depth + 1);
        }

        text.Append('\n');
        AppendIndent(text, depth);
        text.Append('}');
    }

    private static void WriteExpandedArray(StringBuilder text, JsonElement element, int depth)
    {
        if (element.GetArrayLength() == 0)
        {
            text.Append("[]");
            return;
        }

        text.Append('[').Append('\n');
        var isFirst = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!isFirst)
            {
                text.Append(',').Append('\n');
            }

            isFirst = false;
            AppendIndent(text, depth + 1);
            text.Append(item.GetRawText());
        }

        text.Append('\n');
        AppendIndent(text, depth);
        text.Append(']');
    }

    private static void AppendIndent(StringBuilder text, int depth)
    {
        for (var level = 0; level < depth; level++)
        {
            text.Append(Indent);
        }
    }

    /// <summary>Where every value of the document in bytes starts, by path.</summary>
    private static ContentSourceMap MapPositions(byte[] bytes, string fileName)
    {
        var lineStarts = new List<int> { 0 };
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\n')
            {
                lineStarts.Add(index + 1);
            }
        }

        var positions = new Dictionary<string, (int Line, int Column)>(StringComparer.Ordinal);
        var containers = new List<PathFrame>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    containers[^1].PropertyName = reader.GetString()!;
                    continue;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    containers.RemoveAt(containers.Count - 1);
                    AdvanceArrayIndex(containers);
                    continue;
            }

            positions[PathOf(containers)] = PositionOf((int)reader.TokenStartIndex, lineStarts);
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                containers.Add(new PathFrame(reader.TokenType == JsonTokenType.StartArray));
                continue;
            }

            AdvanceArrayIndex(containers);
        }

        return new ContentSourceMap(fileName, positions);
    }

    private static void AdvanceArrayIndex(List<PathFrame> containers)
    {
        if (containers.Count > 0 && containers[^1].IsArray)
        {
            containers[^1].Index++;
        }
    }

    private static string PathOf(List<PathFrame> containers)
    {
        if (containers.Count == 0)
        {
            return string.Empty;
        }

        var path = new StringBuilder();
        foreach (var container in containers)
        {
            if (path.Length > 0)
            {
                path.Append('/');
            }

            if (container.IsArray)
            {
                path.Append(container.Index);
            }
            else
            {
                path.Append(container.PropertyName);
            }
        }

        return path.ToString();
    }

    private static (int Line, int Column) PositionOf(int offset, List<int> lineStarts)
    {
        var index = lineStarts.BinarySearch(offset);
        var lineIndex = index >= 0 ? index : ~index - 1;
        return (lineIndex + 1, offset - lineStarts[lineIndex] + 1);
    }

    private sealed class PathFrame(bool isArray)
    {
        public bool IsArray { get; } = isArray;

        public string PropertyName { get; set; } = string.Empty;

        public int Index { get; set; }
    }
}
