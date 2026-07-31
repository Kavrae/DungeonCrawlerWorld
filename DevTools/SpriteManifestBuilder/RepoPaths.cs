namespace SpriteManifestBuilder;

/// <summary>
/// Finds the repo root by walking up from this tool's own build output looking for
/// DungeonCrawlerWorld.sln -- the same idea DungeonCrawlerWorld/GameLoop.cs's own
/// FindProjectRoot() already uses for its player-activity log, independently reimplemented
/// here since this tool has no project reference to that project. Paths are resolved against
/// the actual source tree (this tool edits source data, not a build output copy).
/// </summary>
public static class RepoPaths
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    public static string SpritesheetsRoot => Path.Combine(RepoRoot, "Content", "Spritesheets");

    public static string ManifestFilePath => Path.Combine(RepoRoot, "Content", "SpriteManifest.json");

    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    private static string RepoRoot => RepoRootLazy.Value;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
