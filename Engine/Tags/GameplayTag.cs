namespace Engine.Tags;

/// <summary>One gameplay tag: a dot-separated name such as "Damage.Fire", whose parent is the name without its last segment.</summary>
/// <remarks>
/// A 2-byte handle to the process-wide intern table (see <see cref="GameplayTagNames"/>), obtained once with
/// <see cref="Get"/> and normally kept in a static readonly field. The default value is <see cref="None"/>, which is
/// not a tag: no set has it and it is nobody's parent. Names are case-sensitive.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct GameplayTag : IEquatable<GameplayTag>
{
    internal GameplayTag(ushort id) => Id = id;

    public static GameplayTag None => default;

    internal ushort Id { get; }

    public bool IsNone => Id == 0;

    /// <summary>The full dot-separated name; empty for None.</summary>
    public string Name => GameplayTagNames.GetEntry(Id).Name;

    /// <summary>The name's last segment ("Fire" for "Damage.Fire"); empty for None.</summary>
    public string LastSegment => GameplayTagNames.GetEntry(Id).LastSegment;

    /// <summary>The tag one segment up, or None for a top-level tag.</summary>
    public GameplayTag Parent => new(GameplayTagNames.GetEntry(Id).ParentId);

    /// <summary>How many segments the name has; 0 for None.</summary>
    public int Depth => GameplayTagNames.GetEntry(Id).Depth;

    /// <summary>The tag named name.</summary>
    /// <exception cref="ArgumentException">name is not one or more dot-separated segments of letters, digits and underscores.</exception>
    public static GameplayTag Get(string name) => new(GameplayTagNames.Intern(name));

    /// <summary>Whether this tag is ancestor or one of its descendants ("Damage.Fire.Lava" is "Damage.Fire").</summary>
    /// <remarks>False when either tag is None.</remarks>
    public bool IsSelfOrDescendantOf(GameplayTag ancestor) => GameplayTagNames.IsSelfOrDescendantOf(Id, ancestor.Id);

    public bool Equals(GameplayTag other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

    public override int GetHashCode() => Id;

    public override string ToString() => IsNone ? "None" : Name;

    public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

    public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
}
