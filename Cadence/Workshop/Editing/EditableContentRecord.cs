using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>
/// One record as the editor holds it: the JSON object a table line or a draft holds, kept whole -- a key its schema
/// doesn't know and a value of the wrong kind stay as written, to be shown and flagged rather than dropped -- and edited
/// in place.
/// </summary>
/// <remarks>
/// It is never the decoded definition: a record is decoded from this (through the real codec) to be previewed, and only
/// a record that decodes and passes its checks is ever written as the codec writes it. A key set here goes in at its
/// place in the schema's order, so a draft reads like the codec's own line. Changed is whether its JSON differs from what
/// it was opened as.
/// <para>
/// Every change goes through its methods, which advance Version; Root is for reading. Json, IsChanged and anything a
/// reader caches against Version are worked out once per change, never per read.
/// </para>
/// </remarks>
public sealed class EditableContentRecord
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private string? _json;
    private int _isChangedVersion = -1;
    private bool _isChanged;

    public EditableContentRecord(string tableName, Type valueType, string json)
    {
        TableName = tableName;
        ValueType = valueType;
        Root = ParseObject(json);
        OriginalJson = Json;
        Id = ReadId(Root);
    }

    /// <summary>The table the record belongs to ("Items").</summary>
    public string TableName { get; }

    /// <summary>The type the table's codec reads the record as.</summary>
    public Type ValueType { get; }

    public Guid Id { get; }

    public JsonObject Root { get; private set; }

    /// <summary>The record's JSON when it was opened (or last marked saved).</summary>
    public string OriginalJson { get; private set; }

    /// <summary>The record's JSON now, compact.</summary>
    public string Json => _json ??= Root.ToJsonString(CompactJson);

    /// <summary>Changes each time the record's JSON, or what it is compared with (IsNew, OriginalJson), does.</summary>
    public int Version { get; private set; }

    /// <summary>Made in this editor and saved nowhere yet, so changed from the start.</summary>
    public bool IsNew { get; private set; }

    public bool IsChanged
    {
        get
        {
            if (_isChangedVersion != Version)
            {
                _isChangedVersion = Version;
                _isChanged = IsNew || Json != OriginalJson;
            }

            return _isChanged;
        }
    }

    /// <summary>A record made in this editor (New), holding json.</summary>
    public static EditableContentRecord CreateNew(string tableName, Type valueType, string json) => new(tableName, valueType, json) { IsNew = true };

    /// <summary>Replaces the whole record with json -- what undo and redo put back.</summary>
    public void Restore(string json)
    {
        Root = ParseObject(json);
        MarkChanged();
    }

    /// <summary>Takes the record as it is now for what it was opened as: nothing is changed any more.</summary>
    public void MarkUnchanged()
    {
        OriginalJson = Json;
        IsNew = false;
        Version++;
    }

    /// <summary>Takes the record as saved nowhere -- its saved lines were removed -- so it is changed until saved again.</summary>
    public void MarkNew()
    {
        IsNew = true;
        Version++;
    }

    private void MarkChanged()
    {
        _json = null;
        Version++;
    }

    /// <summary>The value at path; null when nothing is there.</summary>
    public JsonNode? Find(ContentPath path)
    {
        JsonNode? node = Root;
        foreach (var segment in path.Segments)
        {
            node = segment.IsIndex
                ? node is JsonArray array && segment.Index >= 0 && segment.Index < array.Count ? array[segment.Index] : null
                : node is JsonObject obj && obj.TryGetPropertyValue(segment.Key!, out var child) ? child : null;
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>Sets key of the object at objectPath to value, in its place in schema's order when it is new.</summary>
    /// <exception cref="InvalidOperationException">No object is at objectPath.</exception>
    public void SetField(ContentPath objectPath, ContentSchema schema, string key, JsonNode? value)
    {
        var obj = ObjectAt(objectPath);
        if (obj.ContainsKey(key))
        {
            obj[key] = value;
            MarkChanged();
            return;
        }

        obj.Insert(InsertionIndexOf(obj, schema, key), key, value);
        MarkChanged();
    }

    /// <summary>Takes key out of the object at objectPath, so it means its default; false when it wasn't there.</summary>
    public bool RemoveField(ContentPath objectPath, string key)
    {
        if (!ObjectAt(objectPath).Remove(key))
        {
            return false;
        }

        MarkChanged();
        return true;
    }

    /// <summary>Puts value into the list at key of the object at objectPath, at index (the end for -1), making the list when there is none.</summary>
    public void InsertElement(ContentPath objectPath, ContentSchema schema, string key, int index, JsonNode value)
    {
        var obj = ObjectAt(objectPath);
        if (!obj.TryGetPropertyValue(key, out var existing) || existing is not JsonArray array)
        {
            array = [];
            SetField(objectPath, schema, key, array);
        }

        array.Insert(index < 0 || index > array.Count ? array.Count : index, value);
        MarkChanged();
    }

    /// <summary>Takes element index out of the list at key of the object at objectPath, and the key itself once the list is empty, as the writer leaves an empty list out.</summary>
    public void RemoveElement(ContentPath objectPath, string key, int index)
    {
        var obj = ObjectAt(objectPath);
        if (!obj.TryGetPropertyValue(key, out var existing) || existing is not JsonArray array || index < 0 || index >= array.Count)
        {
            return;
        }

        array.RemoveAt(index);
        if (array.Count == 0)
        {
            obj.Remove(key);
        }

        MarkChanged();
    }

    /// <summary>Replaces element index of the list at key of the object at objectPath with value.</summary>
    public void ReplaceElement(ContentPath objectPath, string key, int index, JsonNode value)
    {
        if (ObjectAt(objectPath)[key] is not JsonArray array || index < 0 || index >= array.Count)
        {
            return;
        }

        array[index] = value;
        MarkChanged();
    }

    /// <summary>Moves element from to to in the list at key of the object at objectPath.</summary>
    public void MoveElement(ContentPath objectPath, string key, int from, int to)
    {
        if (ObjectAt(objectPath)[key] is not JsonArray array || from < 0 || from >= array.Count || to < 0 || to >= array.Count || from == to)
        {
            return;
        }

        var element = array[from];
        array.RemoveAt(from);
        array.Insert(to, element);
        MarkChanged();
    }

    /// <summary>Makes the polymorphic value at objectPath's key a member named memberName, keeping each of its keys newSchema declares with the same kind as oldSchema.</summary>
    public void ChangeType(ContentPath objectPath, ContentSchema containerSchema, string key, string memberName, ContentSchema? oldSchema, ContentSchema newSchema)
    {
        var replacement = new JsonObject { [ContentWriter.DiscriminatorKey] = memberName };
        if (Find(objectPath.Key(key)) is JsonObject old && oldSchema is not null)
        {
            foreach (var field in newSchema.Fields)
            {
                if (old[field.Key] is { } kept && oldSchema.TryGetField(field.Key, out var oldField) && oldField.Kind == field.Kind && oldField.ValueType == field.ValueType)
                {
                    replacement[field.Key] = kept.DeepClone();
                }
            }
        }

        SetField(objectPath, containerSchema, key, replacement);
    }

    /// <summary>The same as ChangeType for an element of a list: the element at index of the list at key.</summary>
    public void ChangeElementType(ContentPath objectPath, string key, int index, string memberName, ContentSchema? oldSchema, ContentSchema newSchema)
    {
        if (ObjectAt(objectPath)[key] is not JsonArray array || index < 0 || index >= array.Count)
        {
            return;
        }

        var replacement = new JsonObject { [ContentWriter.DiscriminatorKey] = memberName };
        if (array[index] is JsonObject old && oldSchema is not null)
        {
            foreach (var field in newSchema.Fields)
            {
                if (old[field.Key] is { } kept && oldSchema.TryGetField(field.Key, out var oldField) && oldField.Kind == field.Kind && oldField.ValueType == field.ValueType)
                {
                    replacement[field.Key] = kept.DeepClone();
                }
            }
        }

        array[index] = replacement;
        MarkChanged();
    }

    private JsonObject ObjectAt(ContentPath path) =>
        Find(path) as JsonObject ?? throw new InvalidOperationException($"No object is at '{path}' of {TableName} record {ContentGuid.Format(Id)}.");

    /// <summary>After every key the object holds that comes before key in schema's order, the discriminator and the record's id first.</summary>
    private static int InsertionIndexOf(JsonObject obj, ContentSchema schema, string key)
    {
        var order = OrderOf(schema, key);
        var index = 0;
        foreach (var property in obj)
        {
            if (property.Key == ContentWriter.DiscriminatorKey || (schema.HasKey(property.Key) && OrderOf(schema, property.Key) < order))
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static int OrderOf(ContentSchema schema, string key)
    {
        for (var index = 0; index < schema.Fields.Count; index++)
        {
            if (schema.Fields[index].Key == key)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new FormatException("A record is a JSON object.");

    private static Guid ReadId(JsonObject root) =>
        root[ContentTable.IdKey] is JsonValue value && value.TryGetValue<string>(out var text) && ContentGuid.TryParse(text, out var id) ? id : Guid.Empty;
}
