using DungeonCrawlerWorld.Workshop;
using Engine.Content;
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

    private static (GameSession Session, string FailureLog) BuildWithMods(params string[] modProjectNames) =>
        BuildOver(WorkshopTestContent.Master, modProjectNames);

    private static (GameSession Session, string FailureLog) BuildOver(ContentLayers content, params string[] modProjectNames)
    {
        var modsDirectory = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var modProjectName in modProjectNames)
            {
                CopyModTo(modsDirectory.FullName, modProjectName);
            }

            var failureLog = new StringWriter();
            var session = ContentSession.Build(ContentSession.ValidateMods(modsDirectory.FullName, content, []), content, [], failureLog);
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
        var (session, _) = BuildOver(GameContent.MasterOnly(MasterManifestDirectory()));

        Assert.IsEmpty(ContentSession.LoadProblems(session));
    }

    [TestMethod]
    public void LoadProblems_ListEachModThatWasDropped_AsAnError_NamingItsFileOrModule()
    {
        var (session, _) = BuildWithMods("Mods.TestFixtures");

        var problems = ContentSession.LoadProblems(session);

        Assert.IsNotEmpty(problems);
        CollectionAssert.AreEqual(session.ModuleFailures.Select(failure => Path.GetFileName(failure.Source)).ToArray(), problems.Select(problem => problem.Source).ToArray());
        Assert.IsTrue(problems.All(problem => problem.Severity == WorkshopProblemSeverity.Error && problem.Description.StartsWith("Mod dropped: ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LoadProblems_ListEachManifestRecordThatDoesNotLoad_ByFileAndLine()
    {
        var masterDirectory = Directory.CreateTempSubdirectory();
        try
        {
            CopyManifest(masterDirectory.FullName);

            File.AppendAllText(Path.Combine(masterDirectory.FullName, GameContent.ItemsFileName), "{ not json\n");

            var problem = ContentSession.LoadProblems(BuildOver(GameContent.MasterOnly(masterDirectory.FullName)).Session).Single();

            Assert.AreEqual(WorkshopProblemSeverity.Error, problem.Severity);
            StringAssert.StartsWith(problem.Source, GameContent.ItemsFileName + ":");
        }
        finally
        {
            TryDeleteDirectory(masterDirectory.FullName);
        }
    }

    /// <summary>The source tree's master manifest, area files included, copied into directory.</summary>
    private static void CopyManifest(string directory)
    {
        var source = MasterManifestDirectory();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(directory, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [TestMethod]
    public void LoadProblems_AnUndefinedSymbolInAnArea_IsListedAtItsRowAndColumn_ForTheMapTab()
    {
        var masterDirectory = Directory.CreateTempSubdirectory();
        try
        {
            CopyManifest(masterDirectory.FullName);
            var areaPath = Path.Combine(masterDirectory.FullName, GameContent.AreasDirectoryName, "starting-area.json");
            var lines = File.ReadAllLines(areaPath);
            var groundLine = Array.FindIndex(lines, line => line.Contains("\"Ground\"", StringComparison.Ordinal)) + 1;
            var quote = lines[groundLine].IndexOf('"');
            lines[groundLine] = lines[groundLine][..(quote + 4)] + "q" + lines[groundLine][(quote + 5)..];
            File.WriteAllLines(areaPath, lines);

            var problems = ContentSession.LoadProblems(BuildOver(GameContent.MasterOnly(masterDirectory.FullName)).Session);

            var problem = problems[0];
            Assert.AreEqual($"{GameContent.AreasDirectoryName}/starting-area.json:{groundLine + 1}:{quote + 5}", problem.Source);
            StringAssert.Contains(problem.Description, "Symbol 'q'");
            Assert.IsNotNull(problem.RecordId);
            Assert.IsTrue(problems.All(each => each.TabTitle == Builders.MapAreas.MapTab.TabTitle), string.Join(Environment.NewLine, problems));
            Assert.IsTrue(problems.Skip(1).All(each => each.Source.StartsWith(GameContent.AreaPlacementsFileName, StringComparison.Ordinal)), string.Join(Environment.NewLine, problems));
        }
        finally
        {
            TryDeleteDirectory(masterDirectory.FullName);
        }
    }

    private static string MasterManifestDirectory() => Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Content", "Definitions", "Master");

    [TestMethod]
    public void AModsRecords_AreLabelledWithTheMod_AndMastersAreNot()
    {
        var modsDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var output = Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "DungeonCrawlerWorld", "Mods", "Mods.ExampleMod", "bin"), "Mods.ExampleMod.dll", SearchOption.AllDirectories).First())!;
            foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(modsDirectory.FullName, "ExampleMod", Path.GetRelativePath(output, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            var content = WorkshopTestContent.Master;
            var session = ContentSession.Build(ContentSession.ValidateMods(modsDirectory.FullName, content, []), content, [], new StringWriter());
            var origins = new Builders.Browsing.ContentOrigins(session.Content);

            Assert.AreEqual("Health Potion [ExampleMod]", origins.Label(GameContent.ItemsTable, Game.Modules.Inventory.BuiltInItemIds.HealthPotion, "Health Potion"));
            Assert.AreEqual("Mana Potion", origins.Label(GameContent.ItemsTable, Game.Modules.Inventory.BuiltInItemIds.ManaPotion, "Mana Potion"));
        }
        finally
        {
            TryDeleteDirectory(modsDirectory.FullName);
        }
    }

    [TestMethod]
    public void Build_HasNoPlayer()
    {
        var (session, _) = BuildWithMods();

        Assert.AreEqual(-1, session.World.PlayerEntityId);
    }
}
