namespace DungeonCrawlerWorld.Workshop;

/// <summary>Where the IDE finds GameShell's built executable: in the source tree, found by walking up to the solution file.</summary>
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
    public static string GameShellOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? AppContext.BaseDirectory
            : Path.Combine(directory.FullName, "DungeonCrawlerWorld", "GameShell", "bin", BuildConfiguration, "net10.0");
    }

    public static string GameExecutablePath() => Path.Combine(GameShellOutputDirectory(), GameExecutableFileName);
}
