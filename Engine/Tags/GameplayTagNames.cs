namespace Engine.Tags;

/// <summary>The process-wide intern table behind <see cref="GameplayTag"/>: each name's id, and each id's name, parent and depth.</summary>
/// <remarks>
/// Holds only names and the parent relation derived from them, so every build in the process can share it; which
/// tags a session accepts is <see cref="GameplayTagRegistry"/>'s. Ids follow intern order, which follows static
/// initialization order, so they differ between runs: nothing may order, save or persist by id. Interning takes a
/// lock; every other read takes the current entries array, which is replaced whole on each intern and never
/// written after it is published.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class GameplayTagNames
{
    internal readonly record struct Entry(string Name, string LastSegment, ushort ParentId, byte Depth);

    private static readonly Lock InternLock = new();
    private static readonly Dictionary<string, ushort> IdsByName = new(StringComparer.Ordinal);
    private static Entry[] _entries = [new Entry("", "", 0, 0)];

    /// <summary>The id of name, interning it and each of its parents first if this is the first time it is seen.</summary>
    /// <exception cref="ArgumentException">name is not one or more dot-separated segments of letters, digits and underscores.</exception>
    /// <exception cref="InvalidOperationException">Every id is already in use.</exception>
    internal static ushort Intern(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (ValidateName(name) is { } rejection)
        {
            throw new ArgumentException($"'{name}' is not a valid gameplay tag name: {rejection}", nameof(name));
        }

        lock (InternLock)
        {
            return InternValidated(name);
        }
    }

    internal static Entry GetEntry(ushort id) => Volatile.Read(ref _entries)[id];

    /// <summary>Whether id is ancestorId or one of its descendants.</summary>
    /// <remarks>False whenever either is 0 (None). Walks up from id only as far as ancestorId's depth.</remarks>
    internal static bool IsSelfOrDescendantOf(ushort id, ushort ancestorId)
    {
        if (id == 0 || ancestorId == 0)
        {
            return false;
        }

        if (id == ancestorId)
        {
            return true;
        }

        var entries = Volatile.Read(ref _entries);
        var ancestorDepth = entries[ancestorId].Depth;
        var currentId = id;
        while (entries[currentId].Depth > ancestorDepth)
        {
            currentId = entries[currentId].ParentId;
        }

        return currentId == ancestorId;
    }

    private static ushort InternValidated(string name)
    {
        if (IdsByName.TryGetValue(name, out var existingId))
        {
            return existingId;
        }

        var lastDotIndex = name.LastIndexOf('.');
        var parentId = lastDotIndex < 0 ? (ushort)0 : InternValidated(name[..lastDotIndex]);
        var entries = _entries;
        if (entries.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Cannot intern gameplay tag '{name}': all {ushort.MaxValue} ids are in use.");
        }

        var id = (ushort)entries.Length;
        var parentDepth = entries[parentId].Depth;
        var grownEntries = new Entry[entries.Length + 1];
        Array.Copy(entries, grownEntries, entries.Length);
        grownEntries[id] = new Entry(name, name[(lastDotIndex + 1)..], parentId, (byte)(parentDepth + 1));
        Volatile.Write(ref _entries, grownEntries);
        IdsByName.Add(name, id);
        return id;
    }

    private static string? ValidateName(string name)
    {
        if (name.Length == 0)
        {
            return "it is empty.";
        }

        var segmentLength = 0;
        var segmentCount = 1;
        foreach (var character in name)
        {
            if (character == '.')
            {
                if (segmentLength == 0)
                {
                    return "it has an empty segment.";
                }

                segmentLength = 0;
                segmentCount++;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return $"'{character}' is not a letter, digit, underscore or '.'.";
            }

            segmentLength++;
        }

        if (segmentLength == 0)
        {
            return "it has an empty segment.";
        }

        return segmentCount > byte.MaxValue ? $"it has more than {byte.MaxValue} segments." : null;
    }
}
