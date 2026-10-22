namespace Tests.Architecture;

[TestClass]
public sealed class WorkshopArchitectureTests
{
    /// <summary>The IDE's projects, relative to the repository root: Cadence Workshop's shell, the game's IDE executable, its Builders tabs, and their tests.</summary>
    private static readonly string[] WorkshopProjectFiles =
    [
        Path.Combine("Cadence", "Workshop", "Workshop.csproj"),
        Path.Combine("DungeonCrawlerWorld", "Workshop", "App", "DungeonCrawlerWorld.Workshop.csproj"),
        Path.Combine("DungeonCrawlerWorld", "Workshop", "Builders", "Builders.csproj"),
        Path.Combine("Workshop.Tests", "Workshop.Tests.csproj"),
    ];

    /// <summary>The game and the engine never depend on the IDE: only the IDE's own projects reference an IDE project, so the game builds, runs and tests without it.</summary>
    [TestMethod]
    public void OnlyWorkshopProjects_ReferenceWorkshopProjects()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var workshopProjectPaths = WorkshopProjectFiles
            .Select(relativePath => Path.GetFullPath(Path.Combine(repositoryRoot, relativePath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var workshopProjectPath in workshopProjectPaths)
        {
            Assert.IsTrue(File.Exists(workshopProjectPath), $"{workshopProjectPath} is listed as an IDE project but doesn't exist.");
        }

        var violations = new List<string>();
        foreach (var projectFile in RepositoryProjects.ProjectFilesUnder(repositoryRoot))
        {
            if (workshopProjectPaths.Contains(Path.GetFullPath(projectFile)))
            {
                continue;
            }

            foreach (var referencedProjectPath in RepositoryProjects.ReferencedProjectPaths(projectFile))
            {
                if (workshopProjectPaths.Contains(referencedProjectPath))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, projectFile)} -> {Path.GetRelativePath(repositoryRoot, referencedProjectPath)}");
                }
            }
        }

        Assert.IsEmpty(violations, $"Only the IDE's own projects may reference an IDE project:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
