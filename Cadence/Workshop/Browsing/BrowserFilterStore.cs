using System.Text.Json;

namespace Workshop.Browsing;

/// <summary>Keeps which browser filter choices each tab has checked, for this user between Workshop runs, in a file of the user's own.</summary>
/// <remarks>
/// The file holds a version (1) and, per tab title, the checked choices' ids; it is read once, when first asked, and
/// written whole on every Save. A missing or unreadable file reads as nothing checked anywhere; a failed write is reported
/// on the error output and the choices stay as checked for this run. Which of the ids a tab still offers is the tab's
/// question: the selection it hands them to leaves out the rest, and the next save forgets them.
/// </remarks>
public sealed class BrowserFilterStore(string filePath)
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private Dictionary<string, IReadOnlyList<string>>? _checkedIdsByTab;

    public string FilePath { get; } = filePath;

    /// <summary>%LOCALAPPDATA%\Cadence\Workshop\gameName\browser-filters.json: one game's filters, apart from every other game's.</summary>
    public static string FilePathForGame(string gameName) => WorkshopUserSettings.FilePathForGame(gameName, "browser-filters.json");

    /// <summary>The ids tabTitle's filter had checked when last saved; none for a tab never saved.</summary>
    public IReadOnlyList<string> CheckedIdsFor(string tabTitle) => CheckedIdsByTab.GetValueOrDefault(tabTitle) ?? [];

    /// <summary>Keeps checkedIds as tabTitle's, and writes every tab's.</summary>
    public void Save(string tabTitle, IReadOnlyList<string> checkedIds)
    {
        if (checkedIds.Count == 0)
        {
            CheckedIdsByTab.Remove(tabTitle);
        }
        else
        {
            CheckedIdsByTab[tabTitle] = [.. checkedIds];
        }

        Write();
    }

    private Dictionary<string, IReadOnlyList<string>> CheckedIdsByTab => _checkedIdsByTab ??= Read();

    private Dictionary<string, IReadOnlyList<string>> Read()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedFilters>(File.ReadAllText(FilePath), JsonOptions);
            var checkedIdsByTab = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (tabTitle, checkedIds) in saved?.Tabs ?? [])
            {
                if (checkedIds is not null)
                {
                    checkedIdsByTab[tabTitle] = [.. checkedIds.Where(id => !string.IsNullOrEmpty(id))];
                }
            }

            return checkedIdsByTab;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Workshop: couldn't read the browser filters at {FilePath}, so none are checked: {exception.Message}");
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var saved = new SavedFilters
            {
                Version = BrowserFilterStore.Version,
                Tabs = CheckedIdsByTab.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToDictionary(entry => entry.Key, entry => (List<string>?)[.. entry.Value]),
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(saved, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Workshop: couldn't save the browser filters to {FilePath}: {exception.Message}");
        }
    }

    private sealed class SavedFilters
    {
        public int Version { get; init; } = 1;

        public Dictionary<string, List<string>?>? Tabs { get; init; }
    }
}
