using System.Text;
using System.Text.Json;

namespace Engine.Json;

/// <summary>Where each value of a JSON file starts, by path: property names and list indices joined by '/' ("lay/Ground/3", "ent/0/x"); the root value is "".</summary>
/// <remarks>For a message that names the line and column of what is wrong in a file a person edits.</remarks>
public sealed class JsonSourceMap
{
    private readonly Dictionary<string, (int Line, int Column)> _positionsByPath;

    private JsonSourceMap(string file, Dictionary<string, (int Line, int Column)> positionsByPath)
    {
        File = file;
        _positionsByPath = positionsByPath;
    }

    /// <summary>What the map's file is called in a message.</summary>
    public string File { get; }

    /// <summary>Where every value of the JSON in bytes starts; file names it. Comments are skipped.</summary>
    /// <exception cref="JsonException">bytes isn't JSON.</exception>
    public static JsonSourceMap Of(byte[] bytes, string file)
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

        return new JsonSourceMap(file, positions);
    }

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
