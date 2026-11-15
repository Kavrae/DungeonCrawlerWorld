using System.Buffers;
using System.Text.Json;
using Engine.Json;
using Engine.Tags;

namespace Engine.Content;

/// <summary>Writes content values as JSON through their registered codecs: keyed fields, defaults omitted, polymorphic values with their discriminator first.</summary>
/// <remarks>
/// A codec's WriteProperties calls the keyed methods here; every key must be declared by the schema of the codec whose
/// object is being written, or the write throws -- a codec can't write a field it doesn't describe -- and the default it
/// passes must be the field's declared ContentField.Default (none for an optional, object or list method). The output is
/// canonical for a given value: keys in the order the codec writes them, floats in their shortest round-trip form, a
/// tag set's names sorted ordinally (a set's own order follows tag ids, which vary between runs), Guids as 22-character
/// base64url.
/// </remarks>
public sealed class ContentWriter
{
    /// <summary>The key a polymorphic value's discriminator is written under, before its properties.</summary>
    public const string DiscriminatorKey = "$";

    private readonly Utf8JsonWriter _json;
    private readonly ContentTypeRegistry _types;
    private readonly Stack<ContentSchema> _schemas = new();
    private readonly ISet<string>? _modulesUsed;

    private ContentWriter(Utf8JsonWriter json, ContentTypeRegistry types, ISet<string>? modulesUsed)
    {
        _json = json;
        _types = types;
        _modulesUsed = modulesUsed;
    }

    /// <summary>One value written as a single JSON object (one manifest line when compact), through T's registered codec.</summary>
    /// <param name="envelope">Writes table-level fields (a disabled record's flag and replacement) after the value's own; null for none.</param>
    /// <param name="header">Writes document-level fields (a one-record file's format and table) before the value's own; null for none.</param>
    /// <param name="modulesUsed">Gains the name of each module whose codec wrote part of the value; null when nobody asks.</param>
    public static byte[] WriteRecord<T>(ContentTypeRegistry types, T value, bool indented = false, Action<Utf8JsonWriter>? envelope = null, Action<Utf8JsonWriter>? header = null, ISet<string>? modulesUsed = null)
        where T : notnull
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, indented ? JsonWriting.Indented : JsonWriting.Compact))
        {
            var writer = new ContentWriter(json, types, modulesUsed);
            writer.WriteObject(types.GetValueCodec(typeof(T)), value, discriminator: null, envelope, header);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public void String(string key, string value, string defaultValue = "")
    {
        var field = FieldOf(key);
        if (field.Default is not string declared || declared != defaultValue)
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (value != defaultValue)
        {
            _json.WriteString(key, value);
        }
    }

    /// <summary>Omitted when value is null.</summary>
    public void OptionalString(string key, string? value)
    {
        CheckNoDefault(key);
        if (value is not null)
        {
            _json.WriteString(key, value);
        }
    }

    public void Integer(string key, long value, long defaultValue = 0)
    {
        var field = FieldOf(key);
        if (field.Default is not long declared || declared != defaultValue)
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    /// <summary>Omitted when value is null; any value, zero included, is written.</summary>
    public void OptionalInteger(string key, long? value)
    {
        CheckNoDefault(key);
        if (value is { } present)
        {
            _json.WriteNumber(key, present);
        }
    }

    public void UnsignedInteger(string key, uint value, uint defaultValue = 0)
    {
        var field = FieldOf(key);
        if (field.Default is not long declared || declared != defaultValue)
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    public void Number(string key, float value, float defaultValue = 0f)
    {
        var field = FieldOf(key);
        if (field.Default is not float declared || declared != defaultValue)
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    public void Boolean(string key, bool value, bool defaultValue = false)
    {
        var field = FieldOf(key);
        if (field.Default is not bool declared || declared != defaultValue)
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (value != defaultValue)
        {
            _json.WriteBoolean(key, value);
        }
    }

    /// <summary>By name, so reordering an enum never changes what a file means. Left out when it is the default, unless the field is required: a required enum has no default worth meaning, so it is always written.</summary>
    public void Enum<TEnum>(string key, TEnum value, TEnum defaultValue = default)
        where TEnum : struct, System.Enum
    {
        var field = FieldOf(key);
        if (field.Default is not TEnum declared || !EqualityComparer<TEnum>.Default.Equals(declared, defaultValue))
        {
            throw CurrentSchema.DefaultMismatch(field, defaultValue);
        }

        if (field.IsRequired || !EqualityComparer<TEnum>.Default.Equals(value, defaultValue))
        {
            _json.WriteString(key, value.ToString());
        }
    }

    /// <summary>Always written: a Guid field is an identity or a reference, never optional by default.</summary>
    public void Guid(string key, Guid value)
    {
        CheckNoDefault(key);
        _json.WriteString(key, ContentGuid.Format(value));
    }

    /// <summary>Omitted when value is null.</summary>
    public void OptionalGuid(string key, Guid? value)
    {
        CheckNoDefault(key);
        if (value is { } present)
        {
            _json.WriteString(key, ContentGuid.Format(present));
        }
    }

    /// <summary>Omitted when tag is None.</summary>
    public void Tag(string key, GameplayTag tag)
    {
        CheckNoDefault(key);
        if (!tag.IsNone)
        {
            _json.WriteString(key, tag.Name);
        }
    }

    /// <summary>The tags' names, sorted; omitted when the set is empty.</summary>
    public void Tags(string key, GameplayTagSet tags)
    {
        CheckNoDefault(key);
        if (tags.IsEmpty)
        {
            return;
        }

        var names = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            names.Add(tag.Name);
        }

        names.Sort(StringComparer.Ordinal);
        _json.WriteStartArray(key);
        foreach (var name in names)
        {
            _json.WriteStringValue(name);
        }

        _json.WriteEndArray();
    }

    /// <summary>A plain value through T's codec; omitted when value is null.</summary>
    public void Object<T>(string key, T? value)
        where T : class
    {
        CheckNoDefault(key);
        if (value is null)
        {
            return;
        }

        _json.WritePropertyName(key);
        WriteObject(_types.GetValueCodec(typeof(T)), value, discriminator: null, envelope: null);
    }

    /// <summary>A plain value through T's codec; always written.</summary>
    public void ValueObject<T>(string key, T value)
        where T : struct
    {
        CheckNoDefault(key);
        _json.WritePropertyName(key);
        WriteObject(_types.GetValueCodec(typeof(T)), value, discriminator: null, envelope: null);
    }

    /// <summary>A member of TBase's family, its discriminator first; omitted when value is null.</summary>
    public void Polymorphic<TBase>(string key, TBase? value)
        where TBase : class
    {
        CheckNoDefault(key);
        if (value is null)
        {
            return;
        }

        _json.WritePropertyName(key);
        WritePolymorphicValue(typeof(TBase), value);
    }

    /// <summary>Each element through its codec -- polymorphic when T is a registered family base, plain otherwise; omitted when the list is empty.</summary>
    public void List<T>(string key, IReadOnlyList<T> values)
        where T : notnull
    {
        CheckNoDefault(key);
        if (values.Count == 0)
        {
            return;
        }

        var isPolymorphic = _types.IsPolymorphicBase(typeof(T));
        var plainCodec = isPolymorphic ? null : _types.GetValueCodec(typeof(T));
        _json.WriteStartArray(key);
        foreach (var value in values)
        {
            if (isPolymorphic)
            {
                WritePolymorphicValue(typeof(T), value);
            }
            else
            {
                WriteObject(plainCodec!, value, discriminator: null, envelope: null);
            }
        }

        _json.WriteEndArray();
    }

    /// <summary>A list of Guids, each base64url; omitted when empty.</summary>
    public void GuidList(string key, IReadOnlyList<Guid> values)
    {
        CheckNoDefault(key);
        if (values.Count == 0)
        {
            return;
        }

        _json.WriteStartArray(key);
        foreach (var value in values)
        {
            _json.WriteStringValue(ContentGuid.Format(value));
        }

        _json.WriteEndArray();
    }

    /// <summary>A list of strings, in order; omitted when empty.</summary>
    public void StringList(string key, IReadOnlyList<string> values)
    {
        CheckNoDefault(key);
        if (values.Count == 0)
        {
            return;
        }

        _json.WriteStartArray(key);
        foreach (var value in values)
        {
            _json.WriteStringValue(value);
        }

        _json.WriteEndArray();
    }

    private void WritePolymorphicValue(Type baseType, object value)
    {
        var (name, codec) = _types.GetMember(baseType, value.GetType());
        WriteObject(codec, value, name, envelope: null);
    }

    private void WriteObject(IContentCodec codec, object value, string? discriminator, Action<Utf8JsonWriter>? envelope, Action<Utf8JsonWriter>? header = null)
    {
        _json.WriteStartObject();
        header?.Invoke(_json);
        if (discriminator is not null)
        {
            _json.WriteString(DiscriminatorKey, discriminator);
        }

        _modulesUsed?.Add(_types.ModuleOf(codec));
        _schemas.Push(codec.Schema);
        codec.WriteProperties(this, value);
        _schemas.Pop();

        envelope?.Invoke(_json);
        _json.WriteEndObject();
    }

    private ContentSchema CurrentSchema => _schemas.Peek();

    /// <summary>The field key names in the schema of the object being written; throws for a key the schema doesn't declare.</summary>
    private ContentField FieldOf(string key) => CurrentSchema.FieldOf(key);

    /// <summary>Checks key is declared with no default value: a field whose omission means nothing at all.</summary>
    private void CheckNoDefault(string key)
    {
        var field = FieldOf(key);
        if (field.Default is not null)
        {
            throw CurrentSchema.DefaultMismatch(field, null);
        }
    }
}
