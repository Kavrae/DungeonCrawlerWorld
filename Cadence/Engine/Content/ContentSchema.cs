using Engine.Tags;

namespace Engine.Content;

/// <summary>What kind of value a content field holds -- what an editor draws for it and how it is written.</summary>
public enum ContentFieldKind : byte
{
    Text,
    Integer,
    Number,
    Boolean,

    /// <summary>A member of ContentField.ValueType, written by name.</summary>
    Enum,

    Guid,

    /// <summary>The Guid of a record in ContentField.ReferencedTable.</summary>
    Reference,

    /// <summary>A packed RGBA colour.</summary>
    Color,

    Tag,
    TagSet,

    /// <summary>A value of ContentField.ValueType, written by its registered codec.</summary>
    Object,

    /// <summary>A value of a registered polymorphic family (ContentField.ValueType is its base), written with its discriminator first.</summary>
    Polymorphic,

    /// <summary>A list of ContentField.ElementKind values of ContentField.ValueType.</summary>
    List,
}

/// <summary>One field a codec reads and writes: its key in the file, its name for an editor, its kind, and what limits it.</summary>
/// <param name="Key">The JSON key -- short, since the format is compact.</param>
/// <param name="ValueType">The enum, object or polymorphic base type for Enum, Object and Polymorphic fields, and the element type of a List.</param>
/// <param name="ElementKind">For a List: the kind of each element.</param>
/// <param name="IsRequired">The field must be present; every other field may be omitted, meaning its default.</param>
/// <param name="ReferencedTable">For a Reference (or a List of them): the table the Guid names a record in.</param>
/// <param name="TagDomain">For a Tag or TagSet: the domain every tag it holds must be in -- the reader fails a record holding any other.</param>
/// <param name="Default">
/// What an omitted field means: a string for Text, a long for Integer and Color, a float for Number, a bool for Boolean,
/// the enum's member for Enum. Null when an omitted field holds no value at all -- an optional Text or Integer, and every
/// Guid, Reference, Tag, TagSet, Object, Polymorphic and List. The codec passes the same value to the writer and the reader,
/// and both check it against this one (the writer always, the reader in Debug), so it is never a second, drifting copy.
/// </param>
/// <param name="IsMultiline">For Text: prose an editor shows on several lines (a description); reading and writing are the same either way.</param>
/// <remarks>A DisplayName may end in a hint in parentheses -- "Action lock in frames (none: the entity's own)" -- which an editor shows in the field's tooltip rather than its label.</remarks>
public sealed record ContentField(
    string Key,
    string DisplayName,
    ContentFieldKind Kind,
    Type? ValueType = null,
    ContentFieldKind? ElementKind = null,
    bool IsRequired = false,
    string? ReferencedTable = null,
    double? Minimum = null,
    double? Maximum = null,
    GameplayTagDomain? TagDomain = null,
    object? Default = null,
    bool IsMultiline = false)
{
    public static ContentField Text(string key, string displayName, bool isRequired = false, string defaultValue = "") => new(key, displayName, ContentFieldKind.Text, IsRequired: isRequired, Default: defaultValue);

    /// <summary>A Text field whose omission means no text at all, rather than an empty one.</summary>
    public static ContentField OptionalText(string key, string displayName) => new(key, displayName, ContentFieldKind.Text);

    public static ContentField Integer(string key, string displayName, double? minimum = null, double? maximum = null, long defaultValue = 0) => new(key, displayName, ContentFieldKind.Integer, Minimum: minimum, Maximum: maximum, Default: defaultValue);

    /// <summary>An Integer field whose omission means no number at all; any value it holds, zero included, is written.</summary>
    public static ContentField OptionalInteger(string key, string displayName, double? minimum = null, double? maximum = null) => new(key, displayName, ContentFieldKind.Integer, Minimum: minimum, Maximum: maximum);

    public static ContentField Number(string key, string displayName, double? minimum = null, double? maximum = null, float defaultValue = 0f) => new(key, displayName, ContentFieldKind.Number, Minimum: minimum, Maximum: maximum, Default: defaultValue);

    public static ContentField Boolean(string key, string displayName, bool defaultValue = false) => new(key, displayName, ContentFieldKind.Boolean, Default: defaultValue);

    public static ContentField Enum<TEnum>(string key, string displayName, TEnum defaultValue = default, bool isRequired = false) where TEnum : struct, System.Enum => new(key, displayName, ContentFieldKind.Enum, typeof(TEnum), IsRequired: isRequired, Default: defaultValue);

    public static ContentField Guid(string key, string displayName, bool isRequired = false) => new(key, displayName, ContentFieldKind.Guid, IsRequired: isRequired);

    public static ContentField Reference(string key, string displayName, string referencedTable, bool isRequired = false) => new(key, displayName, ContentFieldKind.Reference, IsRequired: isRequired, ReferencedTable: referencedTable);

    /// <param name="defaultPacked">The packed colour an omitted field means -- the engine knows no colour type, so the game names it.</param>
    public static ContentField Color(string key, string displayName, uint defaultPacked) => new(key, displayName, ContentFieldKind.Color, Default: (long)defaultPacked);

    public static ContentField Tag(string key, string displayName, GameplayTagDomain domain) => new(key, displayName, ContentFieldKind.Tag, TagDomain: domain);

    public static ContentField TagSet(string key, string displayName, GameplayTagDomain domain) => new(key, displayName, ContentFieldKind.TagSet, TagDomain: domain);

    public static ContentField Object<T>(string key, string displayName, bool isRequired = false) => new(key, displayName, ContentFieldKind.Object, typeof(T), IsRequired: isRequired);

    public static ContentField Polymorphic<TBase>(string key, string displayName, bool isRequired = false) => new(key, displayName, ContentFieldKind.Polymorphic, typeof(TBase), IsRequired: isRequired);

    public static ContentField ListOf<T>(string key, string displayName, ContentFieldKind elementKind) => new(key, displayName, ContentFieldKind.List, typeof(T), elementKind);
}

/// <summary>Every field one codec reads and writes, in the order it writes them -- what a generic editor is generated from.</summary>
public sealed class ContentSchema
{
    private readonly Dictionary<string, ContentField> _fieldsByKey;

    public ContentSchema(string typeName, IReadOnlyList<ContentField> fields)
    {
        TypeName = typeName;
        Fields = fields;
        _fieldsByKey = new Dictionary<string, ContentField>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!_fieldsByKey.TryAdd(field.Key, field))
            {
                throw new ArgumentException($"{typeName} declares the key '{field.Key}' twice.", nameof(fields));
            }
        }
    }

    public string TypeName { get; }

    public IReadOnlyList<ContentField> Fields { get; }

    public bool HasKey(string key) => _fieldsByKey.ContainsKey(key);

    public bool TryGetField(string key, out ContentField field) => _fieldsByKey.TryGetValue(key, out field!);

    /// <summary>The field declared under key; throws, naming the type, for a key the schema doesn't declare.</summary>
    public ContentField FieldOf(string key) =>
        _fieldsByKey.TryGetValue(key, out var field)
            ? field
            : throw new InvalidOperationException($"The {TypeName} codec uses '{key}', which its schema doesn't declare.");

    /// <summary>The failure for a codec that treats an omitted field as passed (null for no value) where its schema declares another default.</summary>
    public InvalidOperationException DefaultMismatch(ContentField field, object? passed) =>
        new($"The {TypeName} codec treats an omitted '{field.Key}' as {DescribeDefault(passed)}, but its schema declares {DescribeDefault(field.Default)}.");

    /// <summary>A default as the messages name it: "none" for no value.</summary>
    public static string DescribeDefault(object? value) => value switch
    {
        null => "none",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "none",
    };
}
