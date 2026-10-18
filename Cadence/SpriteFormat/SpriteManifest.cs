using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SpriteFormat;

/// <summary>A loaded sprite manifest: every entry that keeps the rules, by name and in file order, and why each other entry was left out.</summary>
/// <remarks>
/// The game loads Content/SpriteManifest.json through Load; Cadence/Tools/SpriteManifestBuilder reads and writes the same file through
/// ReadEntries and Write, unchecked, so an unfinished or broken file can still be opened and fixed. Both use one model: fields only a
/// tool reads (spans, an unfinished set's empty slots) are content the next edit needs, and the game simply doesn't read them.
///
/// An entry that breaks a rule of SpriteManifestValidation, or repeats a name already loaded, is left out and reported in Problems
/// rather than refusing the whole file: one bad sprite draws its glyph, the way a sprite the manifest lacks does.
/// </remarks>
public sealed class SpriteManifest
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, SpriteManifestEntry> _entriesByName;

    private SpriteManifest(List<SpriteManifestEntry> entries, Dictionary<string, SpriteManifestEntry> entriesByName, List<string> problems)
    {
        Entries = entries;
        _entriesByName = entriesByName;
        Problems = problems;
    }

    /// <summary>Every entry that was loaded, in the order the file lists them.</summary>
    public IReadOnlyList<SpriteManifestEntry> Entries { get; }

    /// <summary>Why each entry left out was left out, in file order; empty when every entry loaded.</summary>
    public IReadOnlyList<string> Problems { get; }

    public bool TryGetEntry(string name, [NotNullWhen(true)] out SpriteManifestEntry? entry) => _entriesByName.TryGetValue(name, out entry);

    /// <summary>Reads and checks the manifest file at path.</summary>
    /// <exception cref="JsonException">The file isn't a manifest's JSON at all.</exception>
    public static SpriteManifest Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Deserializes and checks a manifest, leaving out every entry that breaks a rule or repeats a name.</summary>
    /// <exception cref="JsonException">json isn't a manifest's JSON at all.</exception>
    public static SpriteManifest Parse(string json)
    {
        var entries = new List<SpriteManifestEntry>();
        var entriesByName = new Dictionary<string, SpriteManifestEntry>();
        var problems = new List<string>();

        foreach (var entry in Deserialize(json))
        {
            if (SpriteManifestValidation.FindProblem(entry) is { } problem)
            {
                problems.Add(problem);
                continue;
            }

            if (!entriesByName.TryAdd(entry.Name, entry))
            {
                problems.Add($"Sprite manifest entry '{entry.Name}' appears more than once; each name is one sprite, so only the first is loaded.");
                continue;
            }

            entries.Add(entry);
        }

        return new SpriteManifest(entries, entriesByName, problems);
    }

    /// <summary>The entries of the manifest file at path, unchecked; none when there is no file yet.</summary>
    public static List<SpriteManifestEntry> ReadEntries(string path) =>
        File.Exists(path) ? Deserialize(File.ReadAllText(path)) : [];

    /// <summary>Writes entries to path as the manifest file, indented, in the order given.</summary>
    /// <remarks>Written to a temporary file beside it and then swapped in, so a crash part-way through never leaves a half-written manifest.</remarks>
    public static void Write(string path, IReadOnlyList<SpriteManifestEntry> entries)
    {
        var temporaryPath = path + ".writing";
        File.WriteAllText(temporaryPath, Serialize(entries));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static string Serialize(IReadOnlyList<SpriteManifestEntry> entries) => JsonSerializer.Serialize(entries, WriteOptions);

    private static List<SpriteManifestEntry> Deserialize(string json) => JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];
}
