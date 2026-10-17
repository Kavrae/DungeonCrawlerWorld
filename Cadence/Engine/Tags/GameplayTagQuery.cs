namespace Engine.Tags;

/// <summary>A reusable test of a tag set: every one of these, at least one of those, none of the rest -- all parent-aware.</summary>
/// <remarks>
/// Immutable, so one instance is shared by everything that holds it (a definition, a shop's rules) rather than built
/// per holder. An empty part doesn't constrain: a query with nothing in any part matches every set.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class GameplayTagQuery(GameplayTagSet requireAll = default, GameplayTagSet requireAny = default, GameplayTagSet exclude = default)
{
    /// <summary>Tags the set must have every one of.</summary>
    public GameplayTagSet RequireAll { get; } = requireAll;

    /// <summary>Tags the set must have at least one of; no constraint when empty.</summary>
    public GameplayTagSet RequireAny { get; } = requireAny;

    /// <summary>Tags the set must have none of.</summary>
    public GameplayTagSet Exclude { get; } = exclude;

    /// <summary>A query matching a set that has every one of tags.</summary>
    public static GameplayTagQuery All(GameplayTagSet tags) => new(requireAll: tags);

    /// <summary>A query matching a set that has at least one of tags.</summary>
    public static GameplayTagQuery Any(GameplayTagSet tags) => new(requireAny: tags);

    /// <summary>A query matching a set that has none of tags.</summary>
    public static GameplayTagQuery None(GameplayTagSet tags) => new(exclude: tags);

    public bool Matches(GameplayTagSet tags) =>
        tags.HasAll(RequireAll)
        && (RequireAny.IsEmpty || tags.HasAny(RequireAny))
        && tags.HasNone(Exclude);

    public override string ToString() => $"All {RequireAll}, Any {RequireAny}, None {Exclude}";
}
