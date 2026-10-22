using System.Text.Json;
using Engine.Tags;

namespace Engine.Content;

/// <summary>One JSON object of content, read through the codec its schema belongs to: keyed fields with their defaults, nested values through their own codecs.</summary>
/// <remarks>
/// Made only by ContentObject.Read/ReadPolymorphic, which first check that every key the object holds is declared by
/// the codec's schema (an unknown key fails the record rather than being dropped: it is a typo or a field from a newer
/// codec, and either way the record wouldn't mean what was written). A missing field is its default; a malformed one, a
/// required one absent, an unknown discriminator or a reference to nothing throws ContentFormatException naming it.
/// </remarks>
public readonly struct ContentObject
{
    private readonly JsonElement _element;
    private readonly ContentTypeRegistry _types;
    private readonly IContentReferences _references;
    private readonly string _typeName;

    private ContentObject(JsonElement element, ContentTypeRegistry types, IContentReferences references, string typeName)
    {
        _element = element;
        _types = types;
        _references = references;
        _typeName = typeName;
    }

    /// <summary>Reads element as a T through T's registered codec.</summary>
    /// <param name="envelopeKeys">Table-level keys allowed beside the codec's own (a record's disabled flag and replacement).</param>
    public static T Read<T>(JsonElement element, ContentTypeRegistry types, IContentReferences references, IReadOnlySet<string>? envelopeKeys = null)
        where T : notnull =>
        (T)ReadWith(types.GetValueCodec(typeof(T)), element, types, references, discriminatorAllowed: false, envelopeKeys);

    /// <summary>Reads element as a member of baseType's family, chosen by its discriminator.</summary>
    public static object ReadPolymorphic(Type baseType, JsonElement element, ContentTypeRegistry types, IContentReferences references)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ContentFormatException($"A {baseType.Name} must be an object.");
        }

        if (!element.TryGetProperty(ContentWriter.DiscriminatorKey, out var discriminator) || discriminator.ValueKind != JsonValueKind.String)
        {
            throw new ContentFormatException($"A {baseType.Name} needs a \"{ContentWriter.DiscriminatorKey}\" naming its type.");
        }

        var name = discriminator.GetString()!;
        if (!types.TryGetMember(baseType, name, out var codec))
        {
            throw new ContentFormatException($"No {baseType.Name} type is registered as \"{name}\".");
        }

        return ReadWith(codec, element, types, references, discriminatorAllowed: true, envelopeKeys: null);
    }

    private static object ReadWith(IContentCodec codec, JsonElement element, ContentTypeRegistry types, IContentReferences references, bool discriminatorAllowed, IReadOnlySet<string>? envelopeKeys)
    {
        var schema = codec.Schema;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ContentFormatException($"A {schema.TypeName} must be an object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            var isDiscriminator = discriminatorAllowed && property.Name == ContentWriter.DiscriminatorKey;
            if (!isDiscriminator && !schema.HasKey(property.Name) && envelopeKeys?.Contains(property.Name) != true)
            {
                throw new ContentFormatException($"{schema.TypeName} has no field \"{property.Name}\".");
            }
        }

        foreach (var field in schema.Fields)
        {
            if (field.IsRequired && !element.TryGetProperty(field.Key, out _))
            {
                throw new ContentFormatException($"{schema.TypeName} needs \"{field.Key}\" ({field.DisplayName}).");
            }
        }

        return codec.Read(new ContentObject(element, types, references, schema.TypeName));
    }

    public string String(string key, string defaultValue = "") =>
        TryGet(key, JsonValueKind.String, out var value) ? value.GetString()! : defaultValue;

    public string? OptionalString(string key) =>
        TryGet(key, JsonValueKind.String, out var value) ? value.GetString() : null;

    public long Integer(string key, long defaultValue = 0) =>
        TryGet(key, JsonValueKind.Number, out var value) ? GetInteger(key, value) : defaultValue;

    public long? OptionalInteger(string key) =>
        TryGet(key, JsonValueKind.Number, out var value) ? GetInteger(key, value) : null;

    public uint UnsignedInteger(string key, uint defaultValue = 0) =>
        TryGet(key, JsonValueKind.Number, out var value)
            ? value.TryGetUInt32(out var result) ? result : throw Malformed(key, "an unsigned integer")
            : defaultValue;

    public float Number(string key, float defaultValue = 0f) =>
        TryGet(key, JsonValueKind.Number, out var value)
            ? value.TryGetSingle(out var result) ? result : throw Malformed(key, "a number")
            : defaultValue;

    public bool Boolean(string key, bool defaultValue = false)
    {
        if (!_element.TryGetProperty(key, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Malformed(key, "true or false"),
        };
    }

    public TEnum Enum<TEnum>(string key, TEnum defaultValue = default)
        where TEnum : struct, System.Enum
    {
        if (!TryGet(key, JsonValueKind.String, out var value))
        {
            return defaultValue;
        }

        var name = value.GetString()!;
        return System.Enum.TryParse<TEnum>(name, ignoreCase: false, out var result) && IsDefinedName(result, name)
            ? result
            : throw new ContentFormatException($"{_typeName}'s \"{key}\" is \"{name}\", which isn't a {typeof(TEnum).Name}.");
    }

    public Guid Guid(string key)
    {
        if (!TryGet(key, JsonValueKind.String, out var value))
        {
            throw new ContentFormatException($"{_typeName} needs \"{key}\".");
        }

        return ContentGuid.TryParse(value.GetString()!, out var result) ? result : throw Malformed(key, "a Guid");
    }

    public Guid? OptionalGuid(string key)
    {
        if (!TryGet(key, JsonValueKind.String, out var value))
        {
            return null;
        }

        return ContentGuid.TryParse(value.GetString()!, out var result) ? result : throw Malformed(key, "a Guid");
    }

    public GameplayTag Tag(string key) =>
        TryGet(key, JsonValueKind.String, out var value) ? GameplayTag.Get(value.GetString()!) : GameplayTag.None;

    public GameplayTagSet Tags(string key)
    {
        if (!TryGet(key, JsonValueKind.Array, out var value))
        {
            return GameplayTagSet.Empty;
        }

        var tags = new List<GameplayTag>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            tags.Add(element.ValueKind == JsonValueKind.String ? GameplayTag.Get(element.GetString()!) : throw Malformed(key, "a list of tag names"));
        }

        return GameplayTagSet.Create(tags.ToArray());
    }

    /// <summary>The record key names in the table it names -- throws when nothing has been loaded under that Guid.</summary>
    public T Reference<T>(string key)
        where T : class
    {
        var id = Guid(key);
        return _references.TryResolve<T>(id, out var value)
            ? value
            : throw new ContentFormatException($"{_typeName}'s \"{key}\" names {ContentGuid.Format(id)}, which no {typeof(T).Name} has.");
    }

    public T? Object<T>(string key)
        where T : class =>
        _element.TryGetProperty(key, out var value) ? (T)ReadWith(_types.GetValueCodec(typeof(T)), value, _types, _references, false, null) : null;

    public T ValueObject<T>(string key, T defaultValue = default)
        where T : struct =>
        _element.TryGetProperty(key, out var value) ? (T)ReadWith(_types.GetValueCodec(typeof(T)), value, _types, _references, false, null) : defaultValue;

    public TBase? Polymorphic<TBase>(string key)
        where TBase : class =>
        _element.TryGetProperty(key, out var value) ? (TBase)ReadPolymorphic(typeof(TBase), value, _types, _references) : null;

    public IReadOnlyList<T> List<T>(string key)
        where T : notnull
    {
        if (!TryGet(key, JsonValueKind.Array, out var value))
        {
            return [];
        }

        var isPolymorphic = _types.IsPolymorphicBase(typeof(T));
        var plainCodec = isPolymorphic ? null : _types.GetValueCodec(typeof(T));
        var results = new List<T>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            results.Add(isPolymorphic
                ? (T)ReadPolymorphic(typeof(T), element, _types, _references)
                : (T)ReadWith(plainCodec!, element, _types, _references, false, null));
        }

        return results;
    }

    public IReadOnlyList<Guid> GuidList(string key)
    {
        if (!TryGet(key, JsonValueKind.Array, out var value))
        {
            return [];
        }

        var results = new List<Guid>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            results.Add(element.ValueKind == JsonValueKind.String && ContentGuid.TryParse(element.GetString()!, out var id) ? id : throw Malformed(key, "a list of Guids"));
        }

        return results;
    }

    public IReadOnlyList<string> StringList(string key)
    {
        if (!TryGet(key, JsonValueKind.Array, out var value))
        {
            return [];
        }

        var results = new List<string>(value.GetArrayLength());
        foreach (var element in value.EnumerateArray())
        {
            results.Add(element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Malformed(key, "a list of strings"));
        }

        return results;
    }

    private bool TryGet(string key, JsonValueKind expectedKind, out JsonElement value)
    {
        if (!_element.TryGetProperty(key, out value))
        {
            return false;
        }

        return value.ValueKind == expectedKind
            ? true
            : throw Malformed(key, expectedKind.ToString().ToLowerInvariant());
    }

    private long GetInteger(string key, JsonElement value) =>
        value.TryGetInt64(out var result) ? result : throw Malformed(key, "an integer");

    private ContentFormatException Malformed(string key, string expected) =>
        new($"{_typeName}'s \"{key}\" must be {expected}.");

    /// <summary>Enum.TryParse also accepts numbers and undefined names joined by commas; a file names members, so anything else is refused.</summary>
    private static bool IsDefinedName<TEnum>(TEnum value, string name)
        where TEnum : struct, System.Enum =>
        !char.IsDigit(name[0]) && name[0] != '-' && (System.Enum.IsDefined(value) || name.Contains(','));
}
