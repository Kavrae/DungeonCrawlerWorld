namespace TestSupport;

/// <summary>Where the source tree is, for a test that reads a checked-in file or a mod's build output.</summary>
public static class TestRepository
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    private static readonly Lazy<string> FoundRoot = new(FindRoot);

    /// <summary>The folder holding the solution, found by walking up from the test assembly.</summary>
    public static string Root => FoundRoot.Value;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test assembly's location.");
    }
}
