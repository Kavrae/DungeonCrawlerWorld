using System.Text.Json.Nodes;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>Puts one difference of a record back as its baseline has it, through the record's own edits.</summary>
/// <remarks>
/// A value the baseline leaves out is taken out; any other is written as the baseline wrote it. A removed list element
/// goes back at its old index (or the end), a moved one back to its old index, an added one is taken away. A difference
/// whose place the record no longer has changes nothing.
/// </remarks>
public static class ContentRecordRevert
{
    private static readonly ContentSchema NoSchema = new(string.Empty, []);

    public static void Revert(EditableContentRecord record, ContentTypeRegistry types, ContentFieldDifference difference)
    {
        var baselineValue = difference.BaselineJson is { } json ? JsonNode.Parse(json) : null;
        switch (difference.Kind)
        {
            case ContentDifferenceKind.Changed when difference.CurrentPath is { IsRoot: false } path:
                Put(record, types, path, baselineValue);
                return;
            case ContentDifferenceKind.Added when difference.CurrentPath is { IsRoot: false } path:
                Put(record, types, path, null);
                return;
            case ContentDifferenceKind.Removed when difference.IsRemovedElement && difference.RemovedFrom is { IsRoot: false } list && baselineValue is not null:
                if (!list.Last.IsIndex && record.Find(list.Parent) is JsonObject)
                {
                    var count = record.Find(list) is JsonArray array ? array.Count : 0;
                    record.InsertElement(list.Parent, SchemaAt(record, types, list.Parent), list.Last.Key!, System.Math.Min(difference.BaselinePath!.Last.Index, count), baselineValue);
                }

                return;
            case ContentDifferenceKind.Removed when difference.CurrentPlace is { IsRoot: false } place:
                Put(record, types, place, baselineValue);
                return;
            case ContentDifferenceKind.Moved when difference.CurrentPath is { IsRoot: false } path && path.Last.IsIndex && !path.Parent.Last.IsIndex:
                var listPath = path.Parent;
                if (record.Find(listPath) is JsonArray moved)
                {
                    record.MoveElement(listPath.Parent, listPath.Last.Key!, path.Last.Index, System.Math.Min(difference.BaselinePath!.Last.Index, moved.Count - 1));
                }

                return;
        }
    }

    /// <summary>Writes value at path -- a key of an object or an element of a list --, or takes it out for null.</summary>
    private static void Put(EditableContentRecord record, ContentTypeRegistry types, ContentPath path, JsonNode? value)
    {
        var holder = path.Parent;
        if (path.Last.IsIndex)
        {
            if (holder.IsRoot || holder.Last.IsIndex || record.Find(holder.Parent) is not JsonObject)
            {
                return;
            }

            if (value is null)
            {
                record.RemoveElement(holder.Parent, holder.Last.Key!, path.Last.Index);
                return;
            }

            record.ReplaceElement(holder.Parent, holder.Last.Key!, path.Last.Index, value);
            return;
        }

        if (record.Find(holder) is not JsonObject)
        {
            return;
        }

        if (value is null)
        {
            record.RemoveField(holder, path.Last.Key!);
            return;
        }

        record.SetField(holder, SchemaAt(record, types, holder), path.Last.Key!, value);
    }

    /// <summary>The schema of the object at path in record; an empty one where none is known (a new key then goes at the end).</summary>
    private static ContentSchema SchemaAt(EditableContentRecord record, ContentTypeRegistry types, ContentPath path) =>
        ContentSchemas.ObjectSchemaAt(types, record.ValueType, record.Root, path) ?? NoSchema;
}
