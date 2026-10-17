namespace Tests.Architecture;

[TestClass]
public sealed class EngineRootArchitectureTests
{
    private const string EngineRootFolderName = "Cadence";
    private const string GameRootFolderName = "DungeonCrawlerWorld";

    /// <summary>The engine root is reusable by another game: no project under it references a project under the game root.</summary>
    [TestMethod]
    public void EngineRootProjects_DoNotReferenceGameRootProjects()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var engineRoot = Path.Combine(repositoryRoot, EngineRootFolderName);
        var gameRoot = Path.Combine(repositoryRoot, GameRootFolderName) + Path.DirectorySeparatorChar;

        var engineProjectFiles = RepositoryProjects.ProjectFilesUnder(engineRoot);
        Assert.IsNotEmpty(engineProjectFiles, $"No projects found under {engineRoot}.");

        var violations = new List<string>();
        foreach (var projectFile in engineProjectFiles)
        {
            foreach (var referencedProjectPath in RepositoryProjects.ReferencedProjectPaths(projectFile))
            {
                if (referencedProjectPath.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, projectFile)} -> {Path.GetRelativePath(repositoryRoot, referencedProjectPath)}");
                }
            }
        }

        Assert.IsEmpty(violations, $"Projects under {EngineRootFolderName}/ must not reference projects under {GameRootFolderName}/:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
