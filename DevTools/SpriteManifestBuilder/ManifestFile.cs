using System.Text.Json;

namespace SpriteManifestBuilder;

/// <summary>Reads/writes Content/SpriteManifest.json -- the same file Game/Blueprints/SpriteManifest.cs reads at runtime, via an independent copy of the (de)serialization logic (see ManifestCell's doc comment for why).</summary>
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

    /// <summary>Replaces any existing entry with the same Name, otherwise appends -- the save button's behavior when editing an entry that already exists.</summary>
    public static List<ManifestEntry> Upsert(IReadOnlyList<ManifestEntry> entries, ManifestEntry newEntry)
    {
        var result = entries.Where(entry => entry.Name != newEntry.Name).ToList();
        result.Add(newEntry);
        return result;
    }
}
