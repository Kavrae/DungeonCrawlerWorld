using System.Text.Json;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>
/// The field a whole-record problem is about, read from its message: of the fields the record holds, the one whose label
/// the message names -- "has a wall bounce count (8) but ..." is about Wall bounce count -- so the Error List can take the
/// player to it.
/// </summary>
/// <remarks>
/// A check names a definition, never a path, so this reads the message: every field set in the record, at any depth, is a
/// candidate by its label (less any hint), matched case-insensitively as whole words; the longest label named wins, and of
/// fields with that label the first in the record. A message naming no label locates nothing, and the record alone is
/// opened. Labels shorter than MinimumLabelLength are skipped, since they read as ordinary words.
/// </remarks>
public static class ContentProblemLocator
{
    private const int MinimumLabelLength = 5;

    /// <summary>The path of the field message is about in record; null when it names none the record holds.</summary>
    public static ContentPath? Locate(EditableContentRecord record, ContentTypeRegistry types, string message)
    {
        using var document = JsonDocument.Parse(record.Json);
        var schema = types.GetValueCodec(record.ValueType).Schema;
        var candidates = new List<(ContentPath Path, string Label)>();
        Collect(document.RootElement, schema, ContentPath.Root, types, candidates);

        ContentPath? best = null;
        var bestLength = 0;
        foreach (var (path, label) in candidates)
        {
            if (label.Length > bestLength && NamesWholeWords(message, label))
            {
                best = path;
                bestLength = label.Length;
            }
        }

        return best;
    }

    private static void Collect(JsonElement json, ContentSchema schema, ContentPath path, ContentTypeRegistry types, List<(ContentPath Path, string Label)> candidates)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in schema.Fields)
        {
            if (!json.TryGetProperty(field.Key, out var value))
            {
                continue;
            }

            var fieldPath = path.Key(field.Key);
            var label = ContentFieldLabels.Split(field.DisplayName).Label;
            if (label.Length >= MinimumLabelLength)
            {
                candidates.Add((fieldPath, label));
            }

            if (field.Kind == ContentFieldKind.List && value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var element in value.EnumerateArray())
                {
                    if (ContentSchemas.OfValue(types, field, element) is { } elementSchema)
                    {
                        Collect(element, elementSchema, fieldPath.Element(index), types, candidates);
                    }

                    index++;
                }

                continue;
            }

            if (ContentSchemas.OfValue(types, field, value) is { } valueSchema)
            {
                Collect(value, valueSchema, fieldPath, types, candidates);
            }
        }
    }

    private static bool NamesWholeWords(string message, string label)
    {
        var start = 0;
        while (true)
        {
            var found = message.IndexOf(label, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            var end = found + label.Length;
            if ((found == 0 || !char.IsLetterOrDigit(message[found - 1])) && (end == message.Length || !char.IsLetterOrDigit(message[end])))
            {
                return true;
            }

            start = found + 1;
        }
    }
}
