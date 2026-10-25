using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>Keeps the Debug tab's launch options for this user between Workshop runs, in a file of the user's own -- never in the repository or a content file.</summary>
/// <remarks>A missing or unreadable file reads as DebugLaunchOptions.Default; a failed write is reported on the error output and the options stay as set for this run.</remarks>
public sealed class DebugLaunchOptionsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>%LOCALAPPDATA%\Cadence\Workshop\debug-launch-options.json.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence", "Workshop", "debug-launch-options.json");

    public string FilePath { get; } = filePath;

    public DebugLaunchOptions Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return DebugLaunchOptions.Default;
            }

            var saved = JsonSerializer.Deserialize<SavedOptions>(File.ReadAllText(FilePath), JsonOptions);
            return saved is null ? DebugLaunchOptions.Default : new DebugLaunchOptions(saved.DiagnosticsFeatures & DiagnosticsFeatures.All);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Workshop: couldn't read the debug launch options at {FilePath}, so the defaults are used: {exception.Message}");
            return DebugLaunchOptions.Default;
        }
    }

    public void Save(DebugLaunchOptions options)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new SavedOptions(options.DiagnosticsFeatures), JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Workshop: couldn't save the debug launch options to {FilePath}: {exception.Message}");
        }
    }

    private sealed record SavedOptions(DiagnosticsFeatures DiagnosticsFeatures);
}
