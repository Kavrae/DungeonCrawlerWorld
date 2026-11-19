namespace Engine.ECS.Properties;

/// <summary>What a property's value is, which decides how a consumer shows, edits and saves it.</summary>
public enum ComponentPropertyKind : byte
{
    Integer,
    Number,
    Boolean,
    Text,
    Enum,
    Flags,
    Guid,
    Color,
    Tag,
    TagQuery,
    Nested,
    Definition,
    Indexed,
}

/// <summary>How a property takes part in each consumer.</summary>
[Flags]
public enum ComponentPropertyFlags : byte
{
    None = 0,
    ReadOnly = 1 << 0,
    NotSaved = 1 << 1,
    NotShown = 1 << 2,
    Optional = 1 << 3,
    Derived = 1 << 4,
}

/// <summary>One property of a described type: its stable name, kind, meaning and flags, and the table of a nested one or the row of an indexed one.</summary>
/// <remarks>The name is a contract from the first save on: a save and the diagnostics link key on it.</remarks>
public sealed class ComponentPropertyDescriptor(
    int index,
    string name,
    ComponentPropertyKind kind,
    Type valueType,
    ComponentPropertyMeaning meaning,
    ComponentPropertyFlags flags,
    ComponentPropertyTable? nested = null,
    ComponentPropertyDescriptor? element = null)
{
    public int Index { get; } = index;

    public string Name { get; } = name;

    public ComponentPropertyKind Kind { get; } = kind;

    public Type ValueType { get; } = valueType;

    public ComponentPropertyMeaning Meaning { get; } = meaning;

    public ComponentPropertyFlags Flags { get; } = flags;

    /// <summary>A Nested property's own table.</summary>
    public ComponentPropertyTable? Nested { get; } = nested;

    /// <summary>An Indexed property's row.</summary>
    public ComponentPropertyDescriptor? Element { get; } = element;

    public bool IsReadOnly => (Flags & ComponentPropertyFlags.ReadOnly) != 0;

    public bool IsOptional => (Flags & ComponentPropertyFlags.Optional) != 0;

    public override string ToString() => $"{Name} ({Kind}{(Meaning.IsNone ? "" : $", {Meaning}")}, {Flags})";
}

/// <summary>The ordered properties of one described type.</summary>
public sealed class ComponentPropertyTable
{
    public ComponentPropertyTable(Type describedType, IReadOnlyList<ComponentPropertyDescriptor> properties)
    {
        DescribedType = describedType;
        Properties = properties;
        ShapeHash = HashShape(properties);
    }

    public Type DescribedType { get; }

    public IReadOnlyList<ComponentPropertyDescriptor> Properties { get; }

    /// <summary>A hash of every property's name, kind and nested shape: equal when two tables read and write the same values.</summary>
    public uint ShapeHash { get; }

    private static uint HashShape(IReadOnlyList<ComponentPropertyDescriptor> properties)
    {
        var hash = 2166136261u;
        foreach (var property in properties)
        {
            foreach (var character in property.Name)
            {
                hash = (hash ^ character) * 16777619u;
            }

            hash = (hash ^ (uint)property.Kind) * 16777619u;
            hash = (hash ^ (property.Nested?.ShapeHash ?? 0)) * 16777619u;
            hash = (hash ^ (property.Element is { } element ? (uint)element.Kind + 1 : 0)) * 16777619u;
        }

        return hash;
    }
}

/// <summary>What a value stands for beyond its kind -- a frame, an entity, a session-local id -- named here and resolved by the game.</summary>
public readonly record struct ComponentPropertyMeaning(string? Name)
{
    public static ComponentPropertyMeaning None => default;

    public bool IsNone => Name is null;

    public override string ToString() => Name ?? "None";
}

/// <summary>The meanings the engine names; a game declares its own beside what they name.</summary>
public static class ComponentPropertyMeaningNames
{
    /// <summary>A simulation frame, a deadline or a moment (FrameDeadline.Never for none).</summary>
    public const string Frame = nameof(Frame);

    /// <summary>A length of time in frames.</summary>
    public const string Frames = nameof(Frames);

    /// <summary>An EntityKey, remapped when saved.</summary>
    public const string EntityKey = nameof(EntityKey);

    /// <summary>An entity id: valid this frame only, never saved.</summary>
    public const string EntityId = nameof(EntityId);

    /// <summary>A tile on the map.</summary>
    public const string MapPosition = nameof(MapPosition);

    /// <summary>A distance in tiles.</summary>
    public const string Tiles = nameof(Tiles);

    /// <summary>A behavior's session-local id.</summary>
    public const string BehaviorId = nameof(BehaviorId);
}
