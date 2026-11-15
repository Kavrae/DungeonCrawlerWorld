using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Content;
using Engine.Tags;

namespace Workshop.Editing;

/// <summary>A problem with one value of a record: where it is, by key and by the names an editor shows, and whether it stops the record (an error) or only warns.</summary>
/// <param name="IsMissing">A required value that isn't there at all -- what an editor may count as not filled in yet, rather than wrong.</param>
public sealed record ContentFieldProblem(ContentPath Path, string DisplayPath, string Message, bool IsWarning = false, bool IsMissing = false);

/// <summary>Whether a record a reference names exists -- the editor's answer, over everything it holds, saved or not.</summary>
public interface IContentReferenceLookup
{
    bool Exists(string tableName, Guid id);
}

/// <summary>The schemas a record's values are read with: the table's codec at the top, then each object, polymorphic member and list element's own.</summary>
public static class ContentSchemas
{
    /// <summary>The schema of the object field holds, for a value json of it; null when the field holds no object, or a polymorphic value names no registered member.</summary>
    public static ContentSchema? OfValue(ContentTypeRegistry types, ContentField field, JsonElement json)
    {
        var kind = field.Kind == ContentFieldKind.List ? field.ElementKind : field.Kind;
        return kind switch
        {
            ContentFieldKind.Object when field.ValueType is { } valueType && types.TryGetValueCodec(valueType, out var codec) => codec.Schema,
            ContentFieldKind.Polymorphic when field.ValueType is { } baseType => MemberSchema(types, baseType, json),
            _ => null,
        };
    }

    /// <summary>The schema of the family member json names in its discriminator; null when it names none that is registered.</summary>
    public static ContentSchema? MemberSchema(ContentTypeRegistry types, Type baseType, JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(ContentWriter.DiscriminatorKey, out var name) && name.ValueKind == JsonValueKind.String
            && types.TryGetMember(baseType, name.GetString()!, out var codec)
            ? codec.Schema
            : null;

    /// <summary>The schema of the object at path in a record of recordType whose JSON is root, followed down through each object, polymorphic member and list element; null where none is known.</summary>
    public static ContentSchema? ObjectSchemaAt(ContentTypeRegistry types, Type recordType, JsonNode? root, ContentPath path)
    {
        ContentSchema? schema = types.GetValueCodec(recordType).Schema;
        ContentField? listField = null;
        var node = root;
        foreach (var segment in path.Segments)
        {
            if (segment.IsIndex)
            {
                node = node is JsonArray array && segment.Index < array.Count ? array[segment.Index] : null;
                schema = listField is { } field ? SchemaOfNode(types, field.ElementKind ?? ContentFieldKind.Object, field, node) : null;
                listField = null;
                continue;
            }

            node = (node as JsonObject)?[segment.Key!];
            if (schema is null || !schema.TryGetField(segment.Key!, out var declared))
            {
                return null;
            }

            if (declared.Kind == ContentFieldKind.List)
            {
                listField = declared;
                schema = null;
                continue;
            }

            schema = SchemaOfNode(types, declared.Kind, declared, node);
        }

        return schema;
    }

    /// <summary>The field declaring the value at path, with the kind of value it is there (a list's element kind for an element); null for a path no schema declares (an unknown key, the root's own envelope).</summary>
    public static (ContentField Field, ContentFieldKind Kind)? FieldAt(ContentTypeRegistry types, Type recordType, JsonNode? root, ContentPath path)
    {
        if (path.IsRoot)
        {
            return null;
        }

        if (path.Last.IsIndex)
        {
            return FieldAt(types, recordType, root, path.Parent) is ({ Kind: ContentFieldKind.List } list, _) ? (list, list.ElementKind ?? ContentFieldKind.Object) : null;
        }

        return ObjectSchemaAt(types, recordType, root, path.Parent) is { } schema && schema.TryGetField(path.Last.Key!, out var field) ? (field, field.Kind) : null;
    }

    private static ContentSchema? SchemaOfNode(ContentTypeRegistry types, ContentFieldKind kind, ContentField field, JsonNode? node) => kind switch
    {
        ContentFieldKind.Object when field.ValueType is { } valueType && types.TryGetValueCodec(valueType, out var codec) => codec.Schema,
        ContentFieldKind.Polymorphic when field.ValueType is { } baseType
            && (node as JsonObject)?[ContentWriter.DiscriminatorKey] is JsonValue name && name.TryGetValue<string>(out var memberName)
            && types.TryGetMember(baseType, memberName, out var member) => member.Schema,
        _ => null,
    };

    /// <summary>The discriminator json holds, or null.</summary>
    public static string? DiscriminatorOf(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(ContentWriter.DiscriminatorKey, out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;

    /// <summary>Every tag the session declares that domain accepts (every declared tag for none), by name.</summary>
    public static IReadOnlyList<string> TagNames(ContentTypeRegistry types, GameplayTagDomain? domain) =>
        [.. types.Tags.DeclaredTags.Where(tag => domain is null || types.Tags.Accepts(domain, tag)).Select(tag => tag.Name).Order(StringComparer.Ordinal)];
}

/// <summary>
/// Checks a record against its schemas, field by field, the way the codecs would read it: every problem with the path to
/// its value, so an editor can show it at the field. What the reader refuses is an error; a reference to a record that
/// isn't there is a warning, since a codec that names a record by Guid needn't resolve it (a synthesised spell).
/// </summary>
/// <remarks>
/// A required text equal to its default is missing: the writer leaves the default out, so the record would fail to read
/// back. A value out of its field's declared minimum or maximum is an error, as the editor never makes one. The
/// record's own envelope keys ("off", "rep", "hold") are allowed at its top.
/// </remarks>
public static class ContentSchemaValidation
{
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal) { ContentTable.DisabledKey, ContentTable.ReplacementKey };

    public static IReadOnlyList<ContentFieldProblem> Validate(EditableContentRecord record, ContentTypeRegistry types, IContentReferenceLookup references)
    {
        var problems = new List<ContentFieldProblem>();
        using var document = JsonDocument.Parse(record.Json);
        var schema = types.GetValueCodec(record.ValueType).Schema;
        new Walker(types, references, problems).Object(document.RootElement, schema, ContentPath.Root, string.Empty, allowsEnvelope: true, allowsDiscriminator: false);
        return problems;
    }

    private sealed class Walker(ContentTypeRegistry types, IContentReferenceLookup references, List<ContentFieldProblem> problems)
    {
        public void Object(JsonElement json, ContentSchema schema, ContentPath path, string displayPath, bool allowsEnvelope, bool allowsDiscriminator)
        {
            if (json.ValueKind != JsonValueKind.Object)
            {
                Error(path, displayPath, $"A {schema.TypeName} must be an object.");
                return;
            }

            foreach (var property in json.EnumerateObject())
            {
                if (schema.HasKey(property.Name) || (allowsEnvelope && EnvelopeKeys.Contains(property.Name)) || (allowsDiscriminator && property.Name == ContentWriter.DiscriminatorKey))
                {
                    continue;
                }

                Error(path.Key(property.Name), Join(displayPath, property.Name), $"{schema.TypeName} has no field \"{property.Name}\".");
            }

            foreach (var field in schema.Fields)
            {
                var fieldPath = path.Key(field.Key);
                var fieldDisplay = Join(displayPath, ContentFieldLabels.LabelOf(field));
                if (!json.TryGetProperty(field.Key, out var value))
                {
                    if (field.IsRequired)
                    {
                        problems.Add(new ContentFieldProblem(fieldPath, fieldDisplay, $"{schema.TypeName} needs \"{field.Key}\" ({ContentFieldLabels.LabelOf(field)}).", IsMissing: true));
                    }

                    continue;
                }

                Field(value, field, schema, fieldPath, fieldDisplay);
            }
        }

        private void Field(JsonElement value, ContentField field, ContentSchema schema, ContentPath path, string displayPath)
        {
            if (field.Kind != ContentFieldKind.List)
            {
                Value(value, field.Kind, field, schema, path, displayPath);
                return;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                Error(path, displayPath, $"{schema.TypeName}'s \"{field.Key}\" must be a list.");
                return;
            }

            var index = 0;
            foreach (var element in value.EnumerateArray())
            {
                Value(element, field.ElementKind ?? ContentFieldKind.Object, field, schema, path.Element(index), $"{displayPath}[{index}]");
                index++;
            }
        }

        private void Value(JsonElement value, ContentFieldKind kind, ContentField field, ContentSchema schema, ContentPath path, string displayPath)
        {
            var owner = $"{schema.TypeName}'s \"{field.Key}\"";
            switch (kind)
            {
                case ContentFieldKind.Text:
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        Error(path, displayPath, $"{owner} must be text.");
                    }
                    else if (field.IsRequired && field.Kind == ContentFieldKind.Text && field.Default is string empty && value.GetString() == empty)
                    {
                        Error(path, displayPath, $"{owner} ({ContentFieldLabels.LabelOf(field)}) needs a value.");
                    }

                    break;
                case ContentFieldKind.Integer:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var integer))
                    {
                        Error(path, displayPath, $"{owner} must be a whole number.");
                    }
                    else
                    {
                        Range(integer, field, owner, path, displayPath);
                    }

                    break;
                case ContentFieldKind.Number:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number))
                    {
                        Error(path, displayPath, $"{owner} must be a number.");
                    }
                    else
                    {
                        Range(number, field, owner, path, displayPath);
                    }

                    break;
                case ContentFieldKind.Color:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out _))
                    {
                        Error(path, displayPath, $"{owner} must be a packed colour.");
                    }

                    break;
                case ContentFieldKind.Boolean:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        Error(path, displayPath, $"{owner} must be true or false.");
                    }

                    break;
                case ContentFieldKind.Enum:
                    if (value.ValueKind != JsonValueKind.String || field.ValueType is not { IsEnum: true } enumType || !IsMemberName(enumType, value.GetString()!))
                    {
                        Error(path, displayPath, $"{owner} is {value.GetRawText()}, which isn't a {field.ValueType?.Name}.");
                    }

                    break;
                case ContentFieldKind.Guid:
                    if (value.ValueKind != JsonValueKind.String || !ContentGuid.TryParse(value.GetString()!, out _))
                    {
                        Error(path, displayPath, $"{owner} must be a Guid.");
                    }

                    break;
                case ContentFieldKind.Reference:
                    if (value.ValueKind != JsonValueKind.String || !ContentGuid.TryParse(value.GetString()!, out var id))
                    {
                        Error(path, displayPath, $"{owner} must be a Guid.");
                    }
                    else if (field.ReferencedTable is { } table && !references.Exists(table, id))
                    {
                        problems.Add(new ContentFieldProblem(path, displayPath, $"{owner} names {ContentGuid.Format(id)}, which no record of {table} has.", IsWarning: true));
                    }

                    break;
                case ContentFieldKind.Tag:
                    Tag(value, field, owner, path, displayPath);
                    break;
                case ContentFieldKind.TagSet:
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        Error(path, displayPath, $"{owner} must be a list of tag names.");
                        break;
                    }

                    foreach (var tag in value.EnumerateArray())
                    {
                        Tag(tag, field, owner, path, displayPath);
                    }

                    break;
                case ContentFieldKind.Object:
                    if (field.ValueType is { } objectType && types.TryGetValueCodec(objectType, out var codec))
                    {
                        Object(value, codec.Schema, path, displayPath, allowsEnvelope: false, allowsDiscriminator: false);
                    }

                    break;
                case ContentFieldKind.Polymorphic:
                    Polymorphic(value, field, owner, path, displayPath);
                    break;
            }
        }

        private void Polymorphic(JsonElement value, ContentField field, string owner, ContentPath path, string displayPath)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                Error(path, displayPath, $"{owner} must be an object.");
                return;
            }

            var name = ContentSchemas.DiscriminatorOf(value);
            if (name is null)
            {
                Error(path, displayPath, $"{owner} needs a \"{ContentWriter.DiscriminatorKey}\" naming its type.");
                return;
            }

            if (ContentSchemas.MemberSchema(types, field.ValueType!, value) is not { } member)
            {
                Error(path, displayPath, $"No {field.ValueType!.Name} type is registered as \"{name}\".");
                return;
            }

            Object(value, member, path, $"{displayPath} ({name})", allowsEnvelope: false, allowsDiscriminator: true);
        }

        private void Tag(JsonElement value, ContentField field, string owner, ContentPath path, string displayPath)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                Error(path, displayPath, $"{owner} must hold tag names.");
                return;
            }

            var name = value.GetString()!;
            if (!GameplayTag.TryGet(name, out var tag) || !types.Tags.IsDeclared(tag))
            {
                Error(path, displayPath, $"{owner} holds \"{name}\", which isn't a declared tag.");
                return;
            }

            if (field.TagDomain is { } domain && types.Tags.ProblemWith(domain, tag) is { } problem)
            {
                Error(path, displayPath, $"{owner}: {problem}");
            }
        }

        private void Range(double value, ContentField field, string owner, ContentPath path, string displayPath)
        {
            if (field.Minimum is { } minimum && value < minimum)
            {
                Error(path, displayPath, $"{owner} is {value}, below its minimum {minimum}.");
            }
            else if (field.Maximum is { } maximum && value > maximum)
            {
                Error(path, displayPath, $"{owner} is {value}, above its maximum {maximum}.");
            }
        }

        /// <summary>A member's name, or for a flags enum several joined by ", " -- what ContentObject.Enum reads, and nothing else.</summary>
        private static bool IsMemberName(Type enumType, string text)
        {
            var names = Enum.GetNames(enumType);
            if (names.Contains(text, StringComparer.Ordinal))
            {
                return true;
            }

            return enumType.IsDefined(typeof(FlagsAttribute), inherit: false)
                && text.Split(',').All(part => names.Contains(part.Trim(), StringComparer.Ordinal));
        }

        private void Error(ContentPath path, string displayPath, string message) => problems.Add(new ContentFieldProblem(path, displayPath, message));

        private static string Join(string displayPath, string name) => displayPath.Length == 0 ? name : $"{displayPath} > {name}";
    }
}
