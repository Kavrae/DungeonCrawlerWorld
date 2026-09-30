using System.Runtime.CompilerServices;

namespace Engine.Tags;

/// <summary>An immutable set of gameplay tags, with parent-aware and exact queries.</summary>
/// <remarks>
/// Holds only the tags it was given, not their parents: a parent-aware query walks up each held tag instead. Tags are
/// de-duplicated and kept in the order given, never in id order (ids vary between runs). The one array is allocated
/// when the set is built, so build sets once (a definition, a static field) and never per frame; every query is
/// allocation-free. The default value is the empty set. Equality is order-insensitive.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
[CollectionBuilder(typeof(GameplayTagSet), nameof(Create))]
public readonly struct GameplayTagSet : IEquatable<GameplayTagSet>
{
    private readonly ushort[]? _tagIds;

    private GameplayTagSet(ushort[] tagIds) => _tagIds = tagIds.Length == 0 ? null : tagIds;

    public static GameplayTagSet Empty => default;

    public int Count => _tagIds?.Length ?? 0;

    public bool IsEmpty => _tagIds is null;

    /// <summary>A set of tags, with duplicates dropped.</summary>
    /// <exception cref="ArgumentException">A tag is None.</exception>
    public static GameplayTagSet Create(ReadOnlySpan<GameplayTag> tags)
    {
        if (tags.IsEmpty)
        {
            return default;
        }

        var tagIds = new List<ushort>(tags.Length);
        foreach (var tag in tags)
        {
            if (tag.IsNone)
            {
                throw new ArgumentException("A gameplay tag set cannot hold GameplayTag.None.", nameof(tags));
            }

            if (!tagIds.Contains(tag.Id))
            {
                tagIds.Add(tag.Id);
            }
        }

        return new GameplayTagSet([.. tagIds]);
    }

    /// <summary>Whether a held tag is tag or one of its descendants.</summary>
    /// <remarks>False for None.</remarks>
    public bool Has(GameplayTag tag)
    {
        if (_tagIds is null)
        {
            return false;
        }

        foreach (var tagId in _tagIds)
        {
            if (GameplayTagNames.IsSelfOrDescendantOf(tagId, tag.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether tag itself is held.</summary>
    /// <remarks>False for None.</remarks>
    public bool HasExact(GameplayTag tag) => _tagIds is not null && !tag.IsNone && Array.IndexOf(_tagIds, tag.Id) >= 0;

    /// <summary>Whether this set <see cref="Has"/> at least one of other's tags; false when other is empty.</summary>
    public bool HasAny(GameplayTagSet other)
    {
        if (other._tagIds is null)
        {
            return false;
        }

        foreach (var otherTagId in other._tagIds)
        {
            if (Has(new GameplayTag(otherTagId)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether this set holds at least one of other's tags exactly; false when other is empty.</summary>
    public bool HasAnyExact(GameplayTagSet other)
    {
        if (other._tagIds is null)
        {
            return false;
        }

        foreach (var otherTagId in other._tagIds)
        {
            if (HasExact(new GameplayTag(otherTagId)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether this set <see cref="Has"/> every one of other's tags; true when other is empty.</summary>
    public bool HasAll(GameplayTagSet other)
    {
        if (other._tagIds is null)
        {
            return true;
        }

        foreach (var otherTagId in other._tagIds)
        {
            if (!Has(new GameplayTag(otherTagId)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether this set holds every one of other's tags exactly; true when other is empty.</summary>
    public bool HasAllExact(GameplayTagSet other)
    {
        if (other._tagIds is null)
        {
            return true;
        }

        foreach (var otherTagId in other._tagIds)
        {
            if (!HasExact(new GameplayTag(otherTagId)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether this set has none of other's tags, parent-aware; true when other is empty.</summary>
    public bool HasNone(GameplayTagSet other) => !HasAny(other);

    /// <summary>Whether this set holds none of other's tags exactly; true when other is empty.</summary>
    public bool HasNoneExact(GameplayTagSet other) => !HasAnyExact(other);

    /// <summary>This set with tag added at the end, or this set if tag is already held exactly.</summary>
    /// <exception cref="ArgumentException">tag is None.</exception>
    public GameplayTagSet With(GameplayTag tag)
    {
        if (tag.IsNone)
        {
            throw new ArgumentException("A gameplay tag set cannot hold GameplayTag.None.", nameof(tag));
        }

        if (HasExact(tag))
        {
            return this;
        }

        var tagIds = new ushort[Count + 1];
        _tagIds?.CopyTo(tagIds, 0);
        tagIds[^1] = tag.Id;
        return new GameplayTagSet(tagIds);
    }

    /// <summary>This set followed by every tag of other it doesn't already hold exactly.</summary>
    public GameplayTagSet Union(GameplayTagSet other)
    {
        if (other._tagIds is null || HasAllExact(other))
        {
            return this;
        }

        var tagIds = new List<ushort>(Count + other.Count);
        if (_tagIds is not null)
        {
            tagIds.AddRange(_tagIds);
        }

        foreach (var otherTagId in other._tagIds)
        {
            if (!tagIds.Contains(otherTagId))
            {
                tagIds.Add(otherTagId);
            }
        }

        return new GameplayTagSet([.. tagIds]);
    }

    /// <summary>This set without tag itself; its descendants stay.</summary>
    public GameplayTagSet Without(GameplayTag tag) => Where(tagId => tagId != tag.Id);

    /// <summary>This set without tag and every descendant of it.</summary>
    public GameplayTagSet WithoutDescendantsOf(GameplayTag tag) => Where(tagId => !GameplayTagNames.IsSelfOrDescendantOf(tagId, tag.Id));

    public Enumerator GetEnumerator() => new(_tagIds);

    public bool Equals(GameplayTagSet other) => Count == other.Count && HasAllExact(other);

    public override bool Equals(object? obj) => obj is GameplayTagSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        if (_tagIds is not null)
        {
            foreach (var tagId in _tagIds)
            {
                hash += tagId * -1640531535;
            }
        }

        return hash;
    }

    public override string ToString()
    {
        if (_tagIds is null)
        {
            return "[]";
        }

        return $"[{string.Join(", ", _tagIds.Select(tagId => new GameplayTag(tagId).Name))}]";
    }

    public static bool operator ==(GameplayTagSet left, GameplayTagSet right) => left.Equals(right);

    public static bool operator !=(GameplayTagSet left, GameplayTagSet right) => !left.Equals(right);

    private GameplayTagSet Where(Predicate<ushort> keep)
    {
        if (_tagIds is null)
        {
            return this;
        }

        var keptTagIds = Array.FindAll(_tagIds, keep);
        return keptTagIds.Length == _tagIds.Length ? this : new GameplayTagSet(keptTagIds);
    }

    /// <summary>Enumerates a set's tags in the order they were given, without allocating.</summary>
    /// <cleanupVersion>1</cleanupVersion>
    public struct Enumerator
    {
        private readonly ushort[]? _tagIds;
        private int _index;

        internal Enumerator(ushort[]? tagIds)
        {
            _tagIds = tagIds;
            _index = -1;
        }

        public readonly GameplayTag Current => new(_tagIds![_index]);

        public bool MoveNext() => _tagIds is not null && ++_index < _tagIds.Length;
    }
}
