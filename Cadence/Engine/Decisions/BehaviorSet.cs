namespace Engine.Decisions;

/// <summary>One behavior in a resolved set: the behavior, the grant that gave it, its weight after every modifier, and its session id.</summary>
public readonly record struct BehaviorSetEntry<TContext>(IBehavior<TContext> Behavior, BehaviorGrant<TContext> Grant, float ResolvedWeight, ushort SessionId)
    where TContext : IDecisionContext, allows ref struct
{
    /// <summary>The grant's rank ceiling.</summary>
    public byte Rank => Grant.Rank;
}

/// <summary>Everything a composition of grant lists and modifiers lets an entity do, in the order the arbiter asks: highest rank ceiling first.</summary>
/// <remarks>
/// Immutable, and built once per composition and cached by the caller -- the engine doesn't know what makes a
/// composition (a game's blueprints do). Resolving merges the grant lists in order (a later grant of a behavior replaces
/// an earlier one, Rank 0 takes it away), multiplies each grant's weight by every modifier that targets it, and sorts by
/// rank ceiling, keeping grant order among equal ranks.
/// </remarks>
public sealed class BehaviorSet<TContext>
    where TContext : IDecisionContext, allows ref struct
{
    private readonly BehaviorSetEntry<TContext>[] _entries;

    private BehaviorSet(BehaviorSetEntry<TContext>[] entries)
    {
        _entries = entries;
        foreach (var entry in entries)
        {
            CanCommit |= entry.Grant.CommitFrames > 0;
        }
    }

    /// <summary>Whether any of its grants commits: a caller deciding for an entity of a set that can't needn't read its commitment.</summary>
    public bool CanCommit { get; }

    public static BehaviorSet<TContext> Empty { get; } = new([]);

    /// <summary>Highest rank ceiling first.</summary>
    public ReadOnlySpan<BehaviorSetEntry<TContext>> Entries => _entries;

    public int Count => _entries.Length;

    public bool IsEmpty => _entries.Length == 0;

    public ref readonly BehaviorSetEntry<TContext> this[int index] => ref _entries[index];

    /// <summary>The index of the entry holding the behavior with sessionId; -1 when the set doesn't hold it.</summary>
    public int IndexOf(ushort sessionId)
    {
        for (var index = 0; index < _entries.Length; index++)
        {
            if (_entries[index].SessionId == sessionId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The set grantLists and modifiers resolve to against catalog, which must be frozen.</summary>
    /// <param name="grantLists">Each part of the composition's grants, in the order the parts apply.</param>
    /// <exception cref="InvalidOperationException">catalog isn't frozen; a grant names a behavior catalog doesn't hold; or a modifier names both or neither of a tag and a behavior, or a tag that is undeclared or outside the catalog's behavior domain.</exception>
    public static BehaviorSet<TContext> Resolve(BehaviorCatalog<TContext> catalog, IEnumerable<IReadOnlyList<BehaviorGrant<TContext>>> grantLists, IEnumerable<BehaviorModifier> modifiers)
    {
        if (!catalog.IsFrozen)
        {
            throw new InvalidOperationException("A behavior set resolves only against a frozen catalog.");
        }

        var modifierList = modifiers.ToList();
        foreach (var modifier in modifierList)
        {
            CheckModifier(catalog, modifier);
        }

        var order = new List<Guid>();
        var merged = new Dictionary<Guid, (IBehavior<TContext> Behavior, BehaviorGrant<TContext> Grant)>();
        foreach (var grants in grantLists)
        {
            foreach (var grant in grants)
            {
                if (!catalog.TryGet(grant.BehaviorId, out var behavior))
                {
                    throw new InvalidOperationException($"A behavior grant names {grant.BehaviorId}, which no module registers as a behavior.");
                }

                if (grant.Rank == 0)
                {
                    if (merged.Remove(behavior.Id))
                    {
                        order.Remove(behavior.Id);
                    }

                    continue;
                }

                if (!merged.ContainsKey(behavior.Id))
                {
                    order.Add(behavior.Id);
                }

                merged[behavior.Id] = (behavior, grant);
            }
        }

        var entries = new List<BehaviorSetEntry<TContext>>(merged.Count);
        foreach (var behaviorId in order)
        {
            if (merged.TryGetValue(behaviorId, out var held))
            {
                var weight = held.Grant.Weight;
                foreach (var modifier in modifierList)
                {
                    if (Targets(catalog, modifier, held.Behavior))
                    {
                        weight *= modifier.WeightMultiplier;
                    }
                }

                entries.Add(new BehaviorSetEntry<TContext>(held.Behavior, held.Grant, weight, catalog.SessionIdOf(behaviorId)));
            }
        }

        var sorted = entries.Select((entry, index) => (entry, index)).OrderByDescending(pair => pair.entry.Rank).ThenBy(pair => pair.index).Select(pair => pair.entry).ToArray();
        return new BehaviorSet<TContext>(sorted);
    }

    private static void CheckModifier(BehaviorCatalog<TContext> catalog, BehaviorModifier modifier)
    {
        if ((modifier.Tag is null) == (modifier.BehaviorId is null))
        {
            throw new InvalidOperationException("A behavior modifier names a tag or a behavior, exactly one of them.");
        }

        if (modifier.Tag is { } tag)
        {
            var tags = catalog.Tags!;
            tags.EnsureDeclared([tag], "A behavior modifier");
            tags.EnsureAccepts(catalog.TagDomain!, [tag], "A behavior modifier");
        }
    }

    /// <remarks>A modifier naming a behavior by an old Guid reaches what the Guid redirects to, as a grant does.</remarks>
    private static bool Targets(BehaviorCatalog<TContext> catalog, BehaviorModifier modifier, IBehavior<TContext> behavior) =>
        modifier.Tag is { } tag
            ? behavior.Tags.Has(tag)
            : catalog.TryGet(modifier.BehaviorId!.Value, out var named) && named.Id == behavior.Id;
}
