using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Engine.Persistence;

/// <summary>What a save folder's save.json records about the game that made it: the format, when it was made, its seed, the master content it was made over, and each module's content version then.</summary>
/// <param name="MasterContentHash">ContentLayer.ComputeContentHash of the master layer the save was made over.</param>
/// <param name="ModuleContentVersions">Each module's content version, by module name (ContentTypeRegistry.ModuleContentVersions).</param>
public sealed record SaveHeader(int FormatVersion, DateTimeOffset CreatedUtc, int Seed, string MasterContentHash, IReadOnlyDictionary<string, int> ModuleContentVersions)
{
    /// <summary>The save format this build writes.</summary>
    public const int CurrentFormatVersion = 1;

    private const string FormatKey = "fmt";
    private const string CreatedKey = "created";
    private const string SeedKey = "seed";
    private const string MasterHashKey = "master";
    private const string VersionsKey = "ver";

    /// <summary>How a game about to use this save differs from the one that made it: another format, other master content, or a module at another content version (or one added or gone). Empty when nothing does; the seed and the time never count.</summary>
    public IReadOnlyList<string> DifferencesFrom(SaveHeader current)
    {
        var differences = new List<string>();
        if (FormatVersion != current.FormatVersion)
        {
            differences.Add($"save format {FormatVersion}, this build writes {current.FormatVersion}");
        }

        if (!string.Equals(MasterContentHash, current.MasterContentHash, StringComparison.Ordinal))
        {
            differences.Add("the master content has changed since the save was made");
        }

        foreach (var moduleName in ModuleContentVersions.Keys.Union(current.ModuleContentVersions.Keys).Order(StringComparer.Ordinal))
        {
            var wasPresent = ModuleContentVersions.TryGetValue(moduleName, out var savedVersion);
            var isPresent = current.ModuleContentVersions.TryGetValue(moduleName, out var currentVersion);
            if (!wasPresent)
            {
                differences.Add($"{moduleName} (content version {currentVersion}) wasn't there when the save was made");
            }
            else if (!isPresent)
            {
                differences.Add($"{moduleName} (content version {savedVersion}) was there when the save was made and isn't now");
            }
            else if (savedVersion != currentVersion)
            {
                differences.Add($"{moduleName} was at content version {savedVersion}, now {currentVersion}");
            }
        }

        return differences;
    }

    /// <summary>The header as save.json holds it, indented.</summary>
    public string Write()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Engine.Json.JsonWriting.Indented))
        {
            json.WriteStartObject();
            json.WriteNumber(FormatKey, FormatVersion);
            json.WriteString(CreatedKey, CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            json.WriteNumber(SeedKey, Seed);
            json.WriteString(MasterHashKey, MasterContentHash);
            json.WriteStartObject(VersionsKey);
            foreach (var (moduleName, version) in ModuleContentVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                json.WriteNumber(moduleName, version);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>The header text holds; false, with what is wrong, when it isn't one.</summary>
    public static bool TryRead(string text, out SaveHeader header, out string problem)
    {
        header = null!;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var versions = new Dictionary<string, int>(StringComparer.Ordinal);
            if (root.TryGetProperty(VersionsKey, out var versionsElement))
            {
                foreach (var property in versionsElement.EnumerateObject())
                {
                    versions[property.Name] = property.Value.GetInt32();
                }
            }

            header = new SaveHeader(
                root.GetProperty(FormatKey).GetInt32(),
                DateTimeOffset.Parse(RequiredString(root, CreatedKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                root.GetProperty(SeedKey).GetInt32(),
                RequiredString(root, MasterHashKey),
                versions);
            problem = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            problem = exception.Message;
            return false;
        }
    }

    private static string RequiredString(JsonElement root, string key) =>
        root.GetProperty(key).GetString() ?? throw new FormatException($"\"{key}\" must be a string, not null.");
}
