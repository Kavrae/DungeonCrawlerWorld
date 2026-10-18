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
public sealed record ContentField(
    string Key,
    string DisplayName,
    ContentFieldKind Kind,
    Type? ValueType = null,
    ContentFieldKind? ElementKind = null,
    bool IsRequired = false,
    string? ReferencedTable = null,
    double? Minimum = null,
    double? Maximum = null)
{
    public static ContentField Text(string key, string displayName, bool isRequired = false) => new(key, displayName, ContentFieldKind.Text, IsRequired: isRequired);

    public static ContentField Integer(string key, string displayName, double? minimum = null, double? maximum = null) => new(key, displayName, ContentFieldKind.Integer, Minimum: minimum, Maximum: maximum);

    public static ContentField Number(string key, string displayName, double? minimum = null, double? maximum = null) => new(key, displayName, ContentFieldKind.Number, Minimum: minimum, Maximum: maximum);

    public static ContentField Boolean(string key, string displayName) => new(key, displayName, ContentFieldKind.Boolean);

    public static ContentField Enum<TEnum>(string key, string displayName) where TEnum : struct, System.Enum => new(key, displayName, ContentFieldKind.Enum, typeof(TEnum));

    public static ContentField Guid(string key, string displayName, bool isRequired = false) => new(key, displayName, ContentFieldKind.Guid, IsRequired: isRequired);

    public static ContentField Reference(string key, string displayName, string referencedTable, bool isRequired = false) => new(key, displayName, ContentFieldKind.Reference, IsRequired: isRequired, ReferencedTable: referencedTable);

    public static ContentField Color(string key, string displayName) => new(key, displayName, ContentFieldKind.Color);

    public static ContentField Tag(string key, string displayName) => new(key, displayName, ContentFieldKind.Tag);

    public static ContentField TagSet(string key, string displayName) => new(key, displayName, ContentFieldKind.TagSet);

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
}
