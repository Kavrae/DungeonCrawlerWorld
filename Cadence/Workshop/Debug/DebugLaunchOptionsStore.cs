using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>Keeps the Debug tab's launch options for this user between Workshop runs, in a file of the user's own -- never in the repository or a content file.</summary>
/// <remarks>
/// A missing or unreadable file reads as DebugLaunchOptions.Default; a failed write is reported on the error output and the
/// options stay as set for this run. The file holds a version (2); a field it lacks reads as its default, so a version-1 file
/// (the diagnostics features alone) loads as those features. While the file doesn't exist, the legacy file -- the one every
/// game shared before each had its own -- is read in its place and left as it is.
/// </remarks>
public sealed class DebugLaunchOptionsStore(string filePath, string? legacyFilePath = null)
{
    public const int Version = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>%LOCALAPPDATA%\Cadence\Workshop\debug-launch-options.json, for a game that names no file of its own.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(WorkshopSettingsDirectory, "debug-launch-options.json");

    private static string WorkshopSettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence", "Workshop");

    public string FilePath { get; } = filePath;

    /// <summary>%LOCALAPPDATA%\Cadence\Workshop\gameName\launch-options.json: one game's options, apart from every other game's.</summary>
    public static string FilePathForGame(string gameName) => Path.Combine(WorkshopSettingsDirectory, gameName, "launch-options.json");

    public DebugLaunchOptions Load()
    {
        var path = File.Exists(FilePath) ? FilePath : legacyFilePath;
        if (path is null || !File.Exists(path))
        {
            return DebugLaunchOptions.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<SavedOptions>(File.ReadAllText(path), JsonOptions)?.ToOptions() ?? DebugLaunchOptions.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Workshop: couldn't read the debug launch options at {path}, so the defaults are used: {exception.Message}");
            return DebugLaunchOptions.Default;
        }
    }

    public void Save(DebugLaunchOptions options)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(SavedOptions.From(options), JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Workshop: couldn't save the debug launch options to {FilePath}: {exception.Message}");
        }
    }

    private sealed class SavedOptions
    {
        public int Version { get; init; } = 1;

        public DiagnosticsFeatures DiagnosticsFeatures { get; init; } = DebugLaunchOptions.Default.DiagnosticsFeatures;

        public bool UsesRandomSeed { get; init; } = DebugLaunchOptions.Default.UsesRandomSeed;

        public int Seed { get; init; } = DebugLaunchOptions.Default.Seed;

        public bool RunsBenchmark { get; init; } = DebugLaunchOptions.Default.RunsBenchmark;

        public long BenchmarkStartFrame { get; init; } = DebugLaunchOptions.DefaultBenchmarkFrames.StartFrame;

        public long BenchmarkEndFrame { get; init; } = DebugLaunchOptions.DefaultBenchmarkFrames.EndFrame;

        public bool Headless { get; init; } = DebugLaunchOptions.Default.Headless;

        public string ExtraArguments { get; init; } = DebugLaunchOptions.Default.ExtraArguments;

        public static SavedOptions From(DebugLaunchOptions options) => new()
        {
            Version = DebugLaunchOptionsStore.Version,
            DiagnosticsFeatures = options.DiagnosticsFeatures,
            UsesRandomSeed = options.UsesRandomSeed,
            Seed = options.Seed,
            RunsBenchmark = options.RunsBenchmark,
            BenchmarkStartFrame = options.BenchmarkFrames.StartFrame,
            BenchmarkEndFrame = options.BenchmarkFrames.EndFrame,
            Headless = options.Headless,
            ExtraArguments = options.ExtraArguments,
        };

        /// <summary>The options this holds; a benchmark range that isn't 0 &lt;= start &lt; end reads as the default range.</summary>
        public DebugLaunchOptions ToOptions() => new()
        {
            DiagnosticsFeatures = DiagnosticsFeatures & DiagnosticsFeatures.All,
            UsesRandomSeed = UsesRandomSeed,
            Seed = Seed,
            RunsBenchmark = RunsBenchmark,
            BenchmarkFrames = BenchmarkStartFrame >= 0 && BenchmarkEndFrame > BenchmarkStartFrame
                ? new BenchmarkFrameRange(BenchmarkStartFrame, BenchmarkEndFrame)
                : DebugLaunchOptions.DefaultBenchmarkFrames,
            Headless = Headless,
            ExtraArguments = ExtraArguments ?? string.Empty,
        };
    }
}
