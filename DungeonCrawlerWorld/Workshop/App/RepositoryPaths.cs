using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>Where the IDE finds GameShell -- its project, its built executable and the repository's log folder -- in the source tree, found by walking up to the solution file.</summary>
internal static class RepositoryPaths
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";
    private const string GameExecutableFileName = "DungeonCrawlerWorld.exe";

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    /// <summary>GameShell's build output for the configuration this IDE was built in, or the IDE's own folder when it isn't running from a source tree.</summary>
    public static string GameShellOutputDirectory() =>
        RepositoryRoot() is { } root
            ? Path.Combine(root, "DungeonCrawlerWorld", "GameShell", "bin", BuildConfiguration, "net10.0")
            : AppContext.BaseDirectory;

    public static string GameExecutablePath() => Path.Combine(GameShellOutputDirectory(), GameExecutableFileName);

    /// <summary>Builds GameShell in the IDE's own configuration, with build nodes of its own so Stop can end them without touching another build's; null when the IDE isn't running from a source tree.</summary>
    public static ProcessCommand? GameShellBuildCommand() =>
        RepositoryRoot() is { } root
            ? new ProcessCommand("dotnet", $"build \"{Path.Combine(root, "DungeonCrawlerWorld", "GameShell", "GameShell.csproj")}\" -c {BuildConfiguration} --nologo -nodeReuse:false", root)
            : null;

    /// <summary>The master manifest in the source tree -- the copy the IDE edits; the IDE's own copy when it isn't running from a source tree.</summary>
    public static string MasterContentDirectory() =>
        RepositoryRoot() is { } root
            ? Path.Combine(root, "DungeonCrawlerWorld", "Content", "Definitions", "Master")
            : Path.Combine(AppContext.BaseDirectory, "Definitions", "Master");

    /// <summary>The repository's Log folder, beside the solution; the IDE's own folder when it isn't running from a source tree.</summary>
    public static string LogDirectory() => Path.Combine(RepositoryRoot() ?? AppContext.BaseDirectory, "Log");

    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }
}
