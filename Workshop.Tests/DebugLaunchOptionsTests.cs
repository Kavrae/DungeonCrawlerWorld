using Engine.Diagnostics;
using Engine.Math;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
public sealed class DebugLaunchOptionsTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => Directory.CreateDirectory(_directory = Path.Combine(Path.GetTempPath(), $"launch-options-{Guid.NewGuid():N}"));

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void TheDefaults_PassOnlyTheDiagnosticsFeatures()
    {
        Assert.AreEqual("--diagnostics=frame,gauges", DebugLaunchOptions.Default.Arguments);
        Assert.IsEmpty(DebugLaunchOptions.Default.ProblemsWith([]));
    }

    [TestMethod]
    public void EveryOption_IsPassedInOrder_AndTheGameReadsBackWhatTheyMake()
    {
        var options = DebugLaunchOptions.Default with
        {
            UsesRandomSeed = false,
            Seed = -42,
            RunsBenchmark = true,
            BenchmarkFrames = new BenchmarkFrameRange(100, 900),
            Headless = true,
            ExtraArguments = " --map-size=512 ",
        };

        Assert.AreEqual("--diagnostics=frame,gauges --seed=-42 --benchmark-frames=100-900 --headless --map-size=512", options.Arguments);
        var arguments = CommandLineArguments.Split(options.Arguments);
        Assert.AreEqual(-42, RandomSeed.Parse(arguments));
        Assert.AreEqual(new BenchmarkFrameRange(100, 900), BenchmarkFrameRange.Parse(arguments));
        Assert.IsTrue(HeadlessArgument.IsPresent(arguments));
        Assert.IsFalse(HeadlessArgument.IsRefused(arguments));
    }

    [TestMethod]
    public void ASwitchedOffOption_PassesNothing_ButKeepsItsValue()
    {
        var options = DebugLaunchOptions.Default with { UsesRandomSeed = true, Seed = 99, RunsBenchmark = false, BenchmarkFrames = new BenchmarkFrameRange(5, 50), Headless = true };

        Assert.AreEqual("--diagnostics=frame,gauges", options.Arguments);
        Assert.IsFalse(options.RunsHeadless);
        Assert.AreEqual(99, options.Seed);
        Assert.AreEqual(new BenchmarkFrameRange(5, 50), options.BenchmarkFrames);
    }

    [TestMethod]
    public void ExtraArgumentsRepeatingAnOptionsFlag_AreWarnedAbout_OnlyWhenTheOptionPassesIt()
    {
        var randomSeed = DebugLaunchOptions.Default with { ExtraArguments = "--seed=5" };
        Assert.IsEmpty(randomSeed.ProblemsWith([]));

        var givenSeed = randomSeed with { UsesRandomSeed = false };
        var problem = givenSeed.ProblemsWith([]).Single();
        Assert.AreEqual(DebugLaunchOptions.ExtraArgumentsOption, problem.Option);
        Assert.IsFalse(problem.RefusesLaunch);
        StringAssert.Contains(problem.Message, "--seed=");
        StringAssert.Contains(problem.Message, "Seed's");
    }

    [TestMethod]
    public void ExtraArgumentsNamingTheLinkOrTheFeatures_AreAlwaysWarnedAbout()
    {
        var options = DebugLaunchOptions.Default with { ExtraArguments = "--diagnostics-link=127.0.0.1:5 --DIAGNOSTICS=all" };

        var problems = options.ProblemsWith([]);
        Assert.HasCount(2, problems);
        StringAssert.Contains(problems[0].Message, "the Workshop");
        StringAssert.Contains(problems[1].Message, "Diagnostics features");
    }

    [TestMethod]
    public void TheLaunchTargetsOwnArguments_AreCheckedAheadOfTheOptions()
    {
        var options = DebugLaunchOptions.Default with { UsesRandomSeed = false, Seed = 7, ExtraArguments = "--seed=9" };

        var problems = options.ProblemsWith(["--seed=1"]);

        Assert.HasCount(2, problems);
        Assert.AreEqual("Seed", problems[0].Option);
        StringAssert.Contains(problems[0].Message, "the target's");
        Assert.AreEqual(DebugLaunchOptions.ExtraArgumentsOption, problems[1].Option);
        StringAssert.Contains(problems[1].Message, "the launch target");
    }

    [TestMethod]
    public void HeadlessInTheLaunchTargetWithoutARange_RefusesTheLaunch()
    {
        var problem = DebugLaunchOptions.Default.ProblemsWith([HeadlessArgument.Argument]).Single();

        Assert.IsTrue(problem.RefusesLaunch);
        StringAssert.Contains(problem.Message, "the launch target's arguments");
        Assert.IsEmpty((DebugLaunchOptions.Default with { RunsBenchmark = true }).ProblemsWith([HeadlessArgument.Argument]));
    }

    [TestMethod]
    public void ChangedOptions_AreSavedOnceTheyStopChanging()
    {
        using var launcher = new GameLauncher(new GameLaunchTarget("Game.exe", string.Empty, "."));
        var store = new DebugLaunchOptionsStore(Path.Combine(_directory, "options.json"));
        using var debugging = new WorkshopDebugging(new DiagnosticsLinkListener(), store, launcher);
        var status = new DebugTabHarness.RecordingStatus();

        debugging.Options = DebugLaunchOptions.Default with { Seed = 1 };
        debugging.Update(status);
        debugging.Options = DebugLaunchOptions.Default with { Seed = 2 };
        for (var update = 1; update < WorkshopDebugging.SaveDelayFrames; update++)
        {
            debugging.Update(status);
        }

        Assert.IsFalse(File.Exists(store.FilePath));
        debugging.Update(status);
        Assert.AreEqual(2, store.Load().Seed);
    }

    [TestMethod]
    public void HeadlessInExtraArgumentsWithoutARange_RefusesTheLaunch()
    {
        using var launcher = new GameLauncher(new GameLaunchTarget("Game.exe", string.Empty, "."));
        using var debugging = new WorkshopDebugging(new DiagnosticsLinkListener(), new DebugLaunchOptionsStore(Path.Combine(_directory, "options.json")), launcher);
        Assert.IsNull(debugging.LaunchRefusal);

        debugging.Options = DebugLaunchOptions.Default with { ExtraArguments = "--headless" };
        StringAssert.Contains(debugging.LaunchRefusal, HeadlessArgument.RequiresRangeMessage);
        Assert.AreEqual(WorkshopProblemSeverity.Error, DebugLaunchProblems.Of(debugging.Problems).Single().Severity);

        debugging.Options = debugging.Options with { RunsBenchmark = true };
        Assert.IsNull(debugging.LaunchRefusal);
    }

    [TestMethod]
    public void EveryOption_SurvivesASaveAndLoad()
    {
        var store = new DebugLaunchOptionsStore(Path.Combine(_directory, "options.json"));
        var options = new DebugLaunchOptions
        {
            DiagnosticsFeatures = DiagnosticsFeatures.Memory,
            UsesRandomSeed = false,
            Seed = int.MinValue,
            RunsBenchmark = true,
            BenchmarkFrames = new BenchmarkFrameRange(10, 20),
            Headless = true,
            ExtraArguments = "--setting=A.B=\"x y\"",
        };

        store.Save(options);

        Assert.AreEqual(options, store.Load());
        StringAssert.Contains(File.ReadAllText(store.FilePath), "\"Version\": 2");
    }

    [TestMethod]
    public void AVersionOneFile_LoadsAsItsFeatures_WithEverythingElseAtItsDefault()
    {
        var path = Path.Combine(_directory, "options.json");
        File.WriteAllText(path, "{ \"DiagnosticsFeatures\": \"Memory, Startup\" }");

        Assert.AreEqual(DebugLaunchOptions.Default with { DiagnosticsFeatures = DiagnosticsFeatures.Memory | DiagnosticsFeatures.Startup }, new DebugLaunchOptionsStore(path).Load());
    }

    [TestMethod]
    public void ABenchmarkRangeThatIsNoRange_LoadsAsTheDefaultRange()
    {
        var path = Path.Combine(_directory, "options.json");
        File.WriteAllText(path, "{ \"Version\": 2, \"RunsBenchmark\": true, \"BenchmarkStartFrame\": 50, \"BenchmarkEndFrame\": 10 }");

        var options = new DebugLaunchOptionsStore(path).Load();

        Assert.IsTrue(options.RunsBenchmark);
        Assert.AreEqual(DebugLaunchOptions.DefaultBenchmarkFrames, options.BenchmarkFrames);
    }

    [TestMethod]
    public void TheLegacyFile_IsReadWhileTheGamesOwnIsMissing_AndLeftAsItIs()
    {
        var legacyPath = Path.Combine(_directory, "debug-launch-options.json");
        const string legacyText = "{ \"DiagnosticsFeatures\": \"LeakDetection\" }";
        File.WriteAllText(legacyPath, legacyText);
        var store = new DebugLaunchOptionsStore(Path.Combine(_directory, "Game", "launch-options.json"), legacyPath);

        Assert.AreEqual(DiagnosticsFeatures.LeakDetection, store.Load().DiagnosticsFeatures);

        store.Save(DebugLaunchOptions.Default);
        Assert.AreEqual(DebugLaunchOptions.Default, store.Load());
        Assert.AreEqual(legacyText, File.ReadAllText(legacyPath));
    }

    [TestMethod]
    public void EachGame_HasItsOwnFile_UnderCadenceWorkshop()
    {
        var path = DebugLaunchOptionsStore.FilePathForGame("DungeonCrawlerWorld");

        StringAssert.EndsWith(path, Path.Combine("Cadence", "Workshop", "DungeonCrawlerWorld", "launch-options.json"));
        Assert.AreNotEqual(DebugLaunchOptionsStore.DefaultFilePath, path);
    }
}
