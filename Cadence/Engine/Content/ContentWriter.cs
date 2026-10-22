using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Engine.Tags;

namespace Engine.Content;

/// <summary>Writes content values as JSON through their registered codecs: keyed fields, defaults omitted, polymorphic values with their discriminator first.</summary>
/// <remarks>
/// A codec's WriteProperties calls the keyed methods here; every key must be declared by the schema of the codec whose
/// object is being written, or the write throws -- a codec can't write a field it doesn't describe. The output is
/// canonical for a given value: keys in the order the codec writes them, floats in their shortest round-trip form, a
/// tag set's names sorted ordinally (a set's own order follows tag ids, which vary between runs), Guids as 22-character
/// base64url.
/// </remarks>
public sealed class ContentWriter
{
    /// <summary>The key a polymorphic value's discriminator is written under, before its properties.</summary>
    public const string DiscriminatorKey = "$";

    private static readonly JsonWriterOptions CompactOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };
    private static readonly JsonWriterOptions IndentedOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true };

    private readonly Utf8JsonWriter _json;
    private readonly ContentTypeRegistry _types;
    private readonly Stack<ContentSchema> _schemas = new();

    private ContentWriter(Utf8JsonWriter json, ContentTypeRegistry types)
    {
        _json = json;
        _types = types;
    }

    /// <summary>One value written as a single JSON object (one manifest line when compact), through T's registered codec.</summary>
    /// <param name="envelope">Writes table-level fields (a disabled record's flag and replacement) after the value's own; null for none.</param>
    /// <param name="header">Writes document-level fields (a one-record file's format and table) before the value's own; null for none.</param>
    public static byte[] WriteRecord<T>(ContentTypeRegistry types, T value, bool indented = false, Action<Utf8JsonWriter>? envelope = null, Action<Utf8JsonWriter>? header = null)
        where T : notnull
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, indented ? IndentedOptions : CompactOptions))
        {
            var writer = new ContentWriter(json, types);
            writer.WriteObject(types.GetValueCodec(typeof(T)), value, discriminator: null, envelope, header);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public void String(string key, string value, string defaultValue = "")
    {
        CheckKey(key);
        if (value != defaultValue)
        {
            _json.WriteString(key, value);
        }
    }

    /// <summary>Omitted when value is null.</summary>
    public void OptionalString(string key, string? value)
    {
        CheckKey(key);
        if (value is not null)
        {
            _json.WriteString(key, value);
        }
    }

    public void Integer(string key, long value, long defaultValue = 0)
    {
        CheckKey(key);
        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    /// <summary>Omitted when value is null; any value, zero included, is written.</summary>
    public void OptionalInteger(string key, long? value)
    {
        CheckKey(key);
        if (value is { } present)
        {
            _json.WriteNumber(key, present);
        }
    }

    public void UnsignedInteger(string key, uint value, uint defaultValue = 0)
    {
        CheckKey(key);
        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    public void Number(string key, float value, float defaultValue = 0f)
    {
        CheckKey(key);
        if (value != defaultValue)
        {
            _json.WriteNumber(key, value);
        }
    }

    public void Boolean(string key, bool value, bool defaultValue = false)
    {
        CheckKey(key);
        if (value != defaultValue)
        {
            _json.WriteBoolean(key, value);
        }
    }

    /// <summary>By name, so reordering an enum never changes what a file means.</summary>
    public void Enum<TEnum>(string key, TEnum value, TEnum defaultValue = default)
        where TEnum : struct, System.Enum
    {
        CheckKey(key);
        if (!EqualityComparer<TEnum>.Default.Equals(value, defaultValue))
        {
            _json.WriteString(key, value.ToString());
        }
    }

    /// <summary>Always written: a Guid field is an identity or a reference, never optional by default.</summary>
    public void Guid(string key, Guid value)
    {
        CheckKey(key);
        _json.WriteString(key, ContentGuid.Format(value));
    }

    /// <summary>Omitted when value is null.</summary>
    public void OptionalGuid(string key, Guid? value)
    {
        CheckKey(key);
        if (value is { } present)
        {
            _json.WriteString(key, ContentGuid.Format(present));
        }
    }

    /// <summary>Omitted when tag is None.</summary>
    public void Tag(string key, GameplayTag tag)
    {
        CheckKey(key);
        if (!tag.IsNone)
        {
            _json.WriteString(key, tag.Name);
        }
    }

    /// <summary>The tags' names, sorted; omitted when the set is empty.</summary>
    public void Tags(string key, GameplayTagSet tags)
    {
        CheckKey(key);
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
        CheckKey(key);
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
        CheckKey(key);
        _json.WritePropertyName(key);
        WriteObject(_types.GetValueCodec(typeof(T)), value, discriminator: null, envelope: null);
    }

    /// <summary>A member of TBase's family, its discriminator first; omitted when value is null.</summary>
    public void Polymorphic<TBase>(string key, TBase? value)
        where TBase : class
    {
        CheckKey(key);
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
        CheckKey(key);
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
        CheckKey(key);
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
        CheckKey(key);
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

        _schemas.Push(codec.Schema);
        codec.WriteProperties(this, value);
        _schemas.Pop();

        envelope?.Invoke(_json);
        _json.WriteEndObject();
    }

    private void CheckKey(string key)
    {
        var schema = _schemas.Peek();
        if (!schema.HasKey(key))
        {
            throw new InvalidOperationException($"The {schema.TypeName} codec writes '{key}', which its schema doesn't declare.");
        }
    }
}
