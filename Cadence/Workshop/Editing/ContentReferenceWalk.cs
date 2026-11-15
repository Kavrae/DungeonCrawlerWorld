using System.Text.Json;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>One record a record names: the table and Guid named, and the field that names it.</summary>
public sealed record ContentFieldReference(string TableName, Guid Id, ContentPath Path, string DisplayPath);

/// <summary>Every record a record names, found by its schemas: each Reference field and list, at any depth, read from the record's JSON whether or not it decodes.</summary>
/// <remarks>
/// The documents' half of what names what (a codec's Reference names its table); a value the schemas can't read -- the
/// wrong kind, an unknown member -- names nothing. What only code names (a blueprint's UsesItems) is the game's own index.
/// </remarks>
public static class ContentReferenceWalk
{
    public static IReadOnlyList<ContentFieldReference> ReferencesOf(EditableContentRecord record, ContentTypeRegistry types)
    {
        var references = new List<ContentFieldReference>();
        using var document = JsonDocument.Parse(record.Json);
        Object(document.RootElement, types.GetValueCodec(record.ValueType).Schema, ContentPath.Root, string.Empty, types, references);
        return references;
    }

    private static void Object(JsonElement json, ContentSchema schema, ContentPath path, string displayPath, ContentTypeRegistry types, List<ContentFieldReference> references)
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
            var fieldDisplay = displayPath.Length == 0 ? ContentFieldLabels.LabelOf(field) : $"{displayPath} > {ContentFieldLabels.LabelOf(field)}";
            if (field.Kind != ContentFieldKind.List)
            {
                Value(value, field.Kind, field, fieldPath, fieldDisplay, types, references);
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var index = 0;
            foreach (var element in value.EnumerateArray())
            {
                Value(element, field.ElementKind ?? ContentFieldKind.Object, field, fieldPath.Element(index), $"{fieldDisplay}[{index}]", types, references);
                index++;
            }
        }
    }

    private static void Value(JsonElement value, ContentFieldKind kind, ContentField field, ContentPath path, string displayPath, ContentTypeRegistry types, List<ContentFieldReference> references)
    {
        switch (kind)
        {
            case ContentFieldKind.Reference when field.ReferencedTable is { } table && value.ValueKind == JsonValueKind.String && ContentGuid.TryParse(value.GetString()!, out var id):
                references.Add(new ContentFieldReference(table, id, path, displayPath));
                break;
            case ContentFieldKind.Object when field.ValueType is { } objectType && types.TryGetValueCodec(objectType, out var codec):
                Object(value, codec.Schema, path, displayPath, types, references);
                break;
            case ContentFieldKind.Polymorphic when field.ValueType is { } baseType && ContentSchemas.MemberSchema(types, baseType, value) is { } member:
                Object(value, member, path, $"{displayPath} ({ContentSchemas.DiscriminatorOf(value)})", types, references);
                break;
        }
    }
}
