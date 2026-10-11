using System.Text.Json;

namespace SpriteManifestBuilder;

/// <summary>Reads/writes Content/SpriteManifest.json -- the same file Game/Sprites/SpriteManifest.cs reads at runtime, via an independent copy of the (de)serialization logic (see ManifestCell's doc comment for why).</summary>
public static class ManifestFile
{
    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    public static List<ManifestEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ManifestEntry>>(json) ?? [];
    }

    public static void Save(string path, IReadOnlyList<ManifestEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, SaveOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>Replaces any existing entry with the same Name where it stands, otherwise appends -- so re-saving an unchanged entry rewrites the file byte for byte.</summary>
    public static List<ManifestEntry> Upsert(IReadOnlyList<ManifestEntry> entries, ManifestEntry newEntry)
    {
        var result = entries.ToList();
        var existingIndex = result.FindIndex(entry => entry.Name == newEntry.Name);
        if (existingIndex >= 0)
        {
            result[existingIndex] = newEntry;
            return result;
        }

        result.Add(newEntry);
        return result;
    }
}
