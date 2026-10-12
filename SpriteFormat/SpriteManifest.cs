using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SpriteFormat;

/// <summary>A loaded sprite manifest: every entry, checked, by name and in file order.</summary>
/// <remarks>
/// The game loads Content/SpriteManifest.json through Load; DevTools/SpriteManifestBuilder reads and writes the same file through
/// ReadEntries and Write, unchecked, so an unfinished or broken file can still be opened and fixed. Both use one model: fields only a
/// tool reads (spans, an unfinished set's empty slots) are content the next edit needs, and the game simply doesn't read them.
/// </remarks>
public sealed class SpriteManifest
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, SpriteManifestEntry> _entriesByName;

    private SpriteManifest(List<SpriteManifestEntry> entries)
    {
        Entries = entries;
        _entriesByName = new Dictionary<string, SpriteManifestEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (!_entriesByName.TryAdd(entry.Name, entry))
            {
                throw new InvalidDataException($"Sprite manifest entry '{entry.Name}' appears more than once; each name is one sprite.");
            }
        }
    }

    /// <summary>Every entry, in the order the file lists them.</summary>
    public IReadOnlyList<SpriteManifestEntry> Entries { get; }

    public bool TryGetEntry(string name, [NotNullWhen(true)] out SpriteManifestEntry? entry) => _entriesByName.TryGetValue(name, out entry);

    /// <summary>Reads and checks the manifest file at path.</summary>
    /// <exception cref="InvalidDataException">An entry breaks a rule of SpriteManifestValidation, or a name appears twice; the message names it.</exception>
    public static SpriteManifest Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Deserializes and checks a manifest.</summary>
    /// <exception cref="InvalidDataException">An entry breaks a rule of SpriteManifestValidation, or a name appears twice; the message names it.</exception>
    public static SpriteManifest Parse(string json)
    {
        var entries = Deserialize(json);
        foreach (var entry in entries)
        {
            if (SpriteManifestValidation.FindProblem(entry) is { } problem)
            {
                throw new InvalidDataException(problem);
            }
        }

        return new SpriteManifest(entries);
    }

    /// <summary>The entries of the manifest file at path, unchecked; none when there is no file yet.</summary>
    public static List<SpriteManifestEntry> ReadEntries(string path) =>
        File.Exists(path) ? Deserialize(File.ReadAllText(path)) : [];

    /// <summary>Writes entries to path as the manifest file, indented, in the order given.</summary>
    public static void Write(string path, IReadOnlyList<SpriteManifestEntry> entries) =>
        File.WriteAllText(path, Serialize(entries));

    public static string Serialize(IReadOnlyList<SpriteManifestEntry> entries) => JsonSerializer.Serialize(entries, WriteOptions);

    private static List<SpriteManifestEntry> Deserialize(string json) => JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];
}
