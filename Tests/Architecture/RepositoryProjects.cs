using System.Xml.Linq;

namespace Tests.Architecture;

/// <summary>The solution's project files on disk and the projects each one references, read from the source tree.</summary>
internal static class RepositoryProjects
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root from the test assembly's location.");
        }

        return directory.FullName;
    }

    /// <summary>Every project file under folder, build output and logs skipped.</summary>
    public static List<string> ProjectFilesUnder(string folder) =>
        Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
            .Where(projectFile => !IsBuildOutputOrLog(projectFile))
            .ToList();

    /// <summary>The full path of every project projectFile references.</summary>
    public static IEnumerable<string> ReferencedProjectPaths(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        return XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include.Replace('\\', Path.DirectorySeparatorChar))));
    }

    private static bool IsBuildOutputOrLog(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "Log");
}
