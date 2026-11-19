namespace Engine.ECS.Properties;

/// <summary>One value to write into a property: an integer, a number, a flag, text (a tag by name), a Guid, a colour, a reference (a definition or a tag query, which only a restore writes), or nothing (an optional cleared).</summary>
public readonly struct ComponentPropertyValue : IEquatable<ComponentPropertyValue>
{
    private ComponentPropertyValue(ComponentPropertyKind kind, bool isAbsent, long integer, double number, Guid guid, string? text, object? reference = null)
    {
        Kind = kind;
        IsAbsent = isAbsent;
        Integer = integer;
        Number = number;
        Guid = guid;
        Text = text;
        Reference = reference;
    }

    public ComponentPropertyKind Kind { get; }

    public bool IsAbsent { get; }

    public long Integer { get; }

    public double Number { get; }

    public bool Boolean => Integer != 0;

    public Guid Guid { get; }

    public string? Text { get; }

    /// <summary>A definition or a tag query, as a restore writes one.</summary>
    public object? Reference { get; }

    public uint PackedColor => unchecked((uint)Integer);

    public static ComponentPropertyValue Absent => new(ComponentPropertyKind.Integer, true, 0, 0, default, null);

    public static ComponentPropertyValue FromInteger(long value) => new(ComponentPropertyKind.Integer, false, value, 0, default, null);

    public static ComponentPropertyValue FromNumber(double value) => new(ComponentPropertyKind.Number, false, 0, value, default, null);

    public static ComponentPropertyValue FromBoolean(bool value) => new(ComponentPropertyKind.Boolean, false, value ? 1 : 0, 0, default, null);

    public static ComponentPropertyValue FromText(string? value) => new(ComponentPropertyKind.Text, false, 0, 0, default, value);

    public static ComponentPropertyValue FromEnum(long value) => new(ComponentPropertyKind.Enum, false, value, 0, default, null);

    public static ComponentPropertyValue FromGuid(Guid value) => new(ComponentPropertyKind.Guid, false, 0, 0, value, null);

    public static ComponentPropertyValue FromColor(uint packedValue) => new(ComponentPropertyKind.Color, false, packedValue, 0, default, null);

    /// <summary>A tag by its name; an empty name is no tag.</summary>
    public static ComponentPropertyValue FromTag(string name) => new(ComponentPropertyKind.Tag, false, 0, 0, default, name);

    /// <summary>A definition, as a restore writes one (ComponentPropertyAccess.Restore); an edit never takes one.</summary>
    public static ComponentPropertyValue FromDefinition(object definition) => new(ComponentPropertyKind.Definition, false, 0, 0, default, null, definition);

    /// <summary>A tag query, as a restore writes one; an edit never takes one.</summary>
    public static ComponentPropertyValue FromTagQuery(object query) => new(ComponentPropertyKind.TagQuery, false, 0, 0, default, null, query);

    /// <summary>Whether both hold the same value of the same kind: a number by its bits (NaN equals NaN), text by ordinal, a reference by the object's own equality.</summary>
    public bool Equals(ComponentPropertyValue other) =>
        Kind == other.Kind
        && IsAbsent == other.IsAbsent
        && Integer == other.Integer
        && Number.Equals(other.Number)
        && Guid == other.Guid
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Equals(Reference, other.Reference);

    public override bool Equals(object? obj) => obj is ComponentPropertyValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, IsAbsent, Integer, Number, Guid, Text, Reference);

    public static bool operator ==(ComponentPropertyValue left, ComponentPropertyValue right) => left.Equals(right);

    public static bool operator !=(ComponentPropertyValue left, ComponentPropertyValue right) => !left.Equals(right);

    public override string ToString() => IsAbsent ? "Absent" : Kind switch
    {
        ComponentPropertyKind.Number => $"{Kind} {Number}",
        ComponentPropertyKind.Guid => $"{Kind} {Guid}",
        ComponentPropertyKind.Text or ComponentPropertyKind.Tag => $"{Kind} {Text}",
        ComponentPropertyKind.Definition or ComponentPropertyKind.TagQuery => $"{Kind} {Reference}",
        _ => $"{Kind} {Integer}",
    };
}
