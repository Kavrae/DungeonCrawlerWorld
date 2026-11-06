using System.Text;
using System.Text.Json;

namespace Engine.Json;

/// <summary>Lays a JSON value out for a person to read and a diff to show the line that changed: an object outside a list one field per line, a list one element per line, anything inside a list compact on its line.</summary>
/// <remarks>
/// So a list of strings (a map area's rows) is one string per line, and a list of objects (its placements) one object per
/// line. Indented two spaces a level; canonical for a given value, so writing the same value twice gives the same text.
/// </remarks>
public static class JsonExpandedLayout
{
    private const string Indent = "  ";

    /// <summary>element laid out, with no trailing line break.</summary>
    public static string Write(JsonElement element)
    {
        var text = new StringBuilder();
        WriteValue(text, element, depth: 0);
        return text.ToString();
    }

    private static void WriteValue(StringBuilder text, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(text, element, depth);
                break;
            case JsonValueKind.Array:
                WriteArray(text, element, depth);
                break;
            default:
                text.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteObject(StringBuilder text, JsonElement element, int depth)
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
            text.Append('"').Append(JsonWriting.Encoder.Encode(property.Name)).Append("\": ");
            WriteValue(text, property.Value, depth + 1);
        }

        text.Append('\n');
        AppendIndent(text, depth);
        text.Append('}');
    }

    private static void WriteArray(StringBuilder text, JsonElement element, int depth)
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
}
