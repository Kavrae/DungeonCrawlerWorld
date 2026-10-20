using DungeonCrawlerWorld.Workshop;
using Game.Bootstrap;
using Game.Content;

namespace Workshop.Tests;

[TestClass]
public sealed class ContentSessionTests
{
    private const string SolutionFileName = "DungeonCrawlerWorld.sln";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test assembly's location.");
    }

    private static void CopyModTo(string modsDirectory, string projectName)
    {
        var sourcePath = Directory.EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Mods", projectName, "bin"),
                $"{projectName}.dll",
                SearchOption.AllDirectories)
            .First();
        File.Copy(sourcePath, Path.Combine(modsDirectory, Path.GetFileName(sourcePath)), overwrite: true);
    }

    /// <summary>ModuleLoader never unloads a mod's AssemblyLoadContext, so its DLL stays locked on Windows for the rest of the run; leaving the folder behind is harmless.</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static (GameSession Session, string FailureLog) BuildWithMods(params string[] modProjectNames)
    {
        var modsDirectory = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var modProjectName in modProjectNames)
            {
                CopyModTo(modsDirectory.FullName, modProjectName);
            }

            var failureLog = new StringWriter();
            var session = ContentSession.Build(modsDirectory.FullName, [], failureLog);
            return (session, failureLog.ToString());
        }
        finally
        {
            TryDeleteDirectory(modsDirectory.FullName);
        }
    }

    [TestMethod]
    public void Build_WithNoMods_HasEveryBuiltInItemAndNoFailures()
    {
        var (session, failureLog) = BuildWithMods();

        Assert.IsEmpty(session.ModuleFailures);
        Assert.AreEqual(string.Empty, failureLog);
        Assert.IsGreaterThan(0, session.Catalogs.ItemCatalog.Count);
        Assert.IsGreaterThan(0, session.Catalogs.ActionCatalog.Count);
    }

    [TestMethod]
    public void Build_LeavesTheStagingWorldUnbuilt()
    {
        var (session, _) = BuildWithMods();

        Assert.IsFalse(session.Internals.SpawnRecordRebuilder.IsStagingWorldBuilt);
    }

    [TestMethod]
    public void Build_WithTheExampleMod_BuildsWithNoFailures()
    {
        var (withoutMod, _) = BuildWithMods();
        var (withMod, failureLog) = BuildWithMods("Mods.ExampleMod");

        Assert.IsEmpty(withMod.ModuleFailures);
        Assert.AreEqual(string.Empty, failureLog);
        Assert.AreEqual(withoutMod.Catalogs.ItemCatalog.Count, withMod.Catalogs.ItemCatalog.Count);
    }

    [TestMethod]
    public void Build_WithAModThatFails_StillBuildsAndLogsTheFailure()
    {
        var (session, failureLog) = BuildWithMods("Mods.TestFixtures");

        Assert.IsNotEmpty(session.ModuleFailures);
        StringAssert.Contains(failureLog, "[ModuleLoad]");
        Assert.IsGreaterThan(0, session.Catalogs.ItemCatalog.Count);
    }

    [TestMethod]
    public void LoadProblems_ForACleanSessionAndTheShippedManifest_IsEmpty()
    {
        var (session, _) = BuildWithMods();

        Assert.IsEmpty(ContentSession.LoadProblems(session, MasterManifestDirectory()));
    }

    [TestMethod]
    public void LoadProblems_ListEachModThatWasDropped_AsAnError_NamingItsFileOrModule()
    {
        var (session, _) = BuildWithMods("Mods.TestFixtures");

        var problems = ContentSession.LoadProblems(session, MasterManifestDirectory());

        Assert.IsNotEmpty(problems);
        CollectionAssert.AreEqual(session.ModuleFailures.Select(failure => Path.GetFileName(failure.Source)).ToArray(), problems.Select(problem => problem.Source).ToArray());
        Assert.IsTrue(problems.All(problem => problem.Severity == WorkshopProblemSeverity.Error && problem.Description.StartsWith("Mod dropped: ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LoadProblems_ListEachManifestRecordThatDoesNotLoad_ByFileAndLine()
    {
        var (session, _) = BuildWithMods();
        var masterDirectory = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var file in Directory.EnumerateFiles(MasterManifestDirectory()))
            {
                File.Copy(file, Path.Combine(masterDirectory.FullName, Path.GetFileName(file)));
            }

            File.AppendAllText(Path.Combine(masterDirectory.FullName, MasterContent.ItemsFileName), "{ not json\n");

            var problem = ContentSession.LoadProblems(session, masterDirectory.FullName).Single();

            Assert.AreEqual(WorkshopProblemSeverity.Error, problem.Severity);
            StringAssert.StartsWith(problem.Source, MasterContent.ItemsFileName + ":");
        }
        finally
        {
            TryDeleteDirectory(masterDirectory.FullName);
        }
    }

    private static string MasterManifestDirectory() => Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Content", "Definitions", "Master");

    [TestMethod]
    public void Build_HasNoPlayer()
    {
        var (session, _) = BuildWithMods();

        Assert.AreEqual(-1, session.World.PlayerEntityId);
    }
}
