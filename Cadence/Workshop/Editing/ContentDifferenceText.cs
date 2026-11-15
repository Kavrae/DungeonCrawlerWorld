using System.Text.Json.Nodes;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>How a difference's values read in an editor: as a reader reads them, never as raw JSON where something shorter says it.</summary>
/// <remarks>
/// A value left out reads as its default ("1 (default)") or "none"; a reference reads as the record's name; text, an enum
/// member and a tag read without quotes; a polymorphic value reads as its member ("DirectHeal"); a tag set or a list of
/// text as its items joined; anything else as written.
/// </remarks>
public static class ContentDifferenceText
{
    /// <summary>What is shown on a side that doesn't hold the value at all.</summary>
    public const string Absent = "-";

    /// <summary>The baseline's side of difference.</summary>
    public static string Baseline(ContentFieldDifference difference, (ContentField Field, ContentFieldKind Kind)? field, Func<string, Guid, string?> recordName) => difference.Kind switch
    {
        ContentDifferenceKind.Added => Absent,
        ContentDifferenceKind.Moved => $"at [{difference.BaselinePath!.Last.Index}]",
        _ => Value(difference.BaselineJson, field, recordName),
    };

    /// <summary>The current version's side of difference.</summary>
    public static string Current(ContentFieldDifference difference, (ContentField Field, ContentFieldKind Kind)? field, Func<string, Guid, string?> recordName) => difference.Kind switch
    {
        ContentDifferenceKind.Removed => Absent,
        ContentDifferenceKind.Moved => $"at [{difference.CurrentPath!.Last.Index}]",
        _ => Value(difference.CurrentJson, field, recordName),
    };

    /// <summary>json as a reader reads it, for the value field declares (null when none does); recordName names a record of a table by its Guid, null when it can't.</summary>
    public static string Value(string? json, (ContentField Field, ContentFieldKind Kind)? field, Func<string, Guid, string?> recordName)
    {
        if (json is null)
        {
            return field?.Field.Default is { } defaultValue ? $"{ContentSchema.DescribeDefault(defaultValue)} (default)" : "none";
        }

        switch (JsonNode.Parse(json))
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                return field is { Kind: ContentFieldKind.Reference, Field.ReferencedTable: { } table } && ContentGuid.TryParse(text, out var id) && recordName(table, id) is { } name
                    ? name
                    : text;
            case JsonObject member when member[ContentWriter.DiscriminatorKey] is JsonValue discriminator && discriminator.TryGetValue<string>(out var memberName):
                return memberName;
            case JsonArray array when array.All(element => element is JsonValue item && item.TryGetValue<string>(out _)):
                return array.Count == 0 ? "none" : string.Join(", ", array.Select(element => element!.GetValue<string>()));
            default:
                return json;
        }
    }
}
