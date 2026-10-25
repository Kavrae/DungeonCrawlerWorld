using Engine.Diagnostics;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DebugRunTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly DebugLaunchOptions HeadlessOptions = DebugLaunchOptions.Default with { RunsBenchmark = true, Headless = true };

    private string _directory = null!;
    private string _optionsPath = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"debug-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _optionsPath = Path.Combine(_directory, "options.json");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private static GameLaunchTarget CmdTarget(string arguments) => new(Cmd, $"{arguments} & rem", Environment.SystemDirectory);

    private static GameLaunchTarget SleepingGame => CmdTarget("/c ping -n 30 127.0.0.1 > nul");

    private static void FrameUntil(DebugTabHarness harness, Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (true)
        {
            harness.Frame();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {description}; the status line said \"{harness.Status.LastMessage}\".");
            }

            Thread.Sleep(5);
        }
    }

    private static void Launch(DebugTabHarness harness, DebugLaunchOptions options)
    {
        harness.Debugging.Options = options;
        Assert.IsTrue(harness.Launcher.TryLaunch(out var status, harness.Debugging.LaunchArguments), status);
        harness.Debugging.NotifyGameLaunched();
    }

    private static DiagnosticsLink ConnectedLink(DebugTabHarness harness)
    {
        var link = new DiagnosticsLink(harness.Debugging.Link.EndPoint, DiagnosticsFeatures.Memory | DiagnosticsFeatures.Gauges, randomSeed: 5, TextWriter.Null);
        link.Start();
        return link;
    }

    [TestMethod]
    public void AHeadlessRun_CountsItsFramesTowardTheRangesEnd_OnTheStatusLineAndTheLiveHeader()
    {
        using var harness = new DebugTabHarness(_optionsPath, launchTarget: SleepingGame);
        Launch(harness, HeadlessOptions);
        using var link = ConnectedLink(harness);
        var tracker = new GaugeTracker([]);
        link.SessionStarted(tracker.Gauges);
        for (var frame = 1191; frame <= 1200; frame++)
        {
            link.RecordFrame(frame, TimeSpan.FromMilliseconds(3), tracker);
        }

        FrameUntil(harness, () => harness.Status.LastMessage == "Headless benchmark: frame 1,200 of 3,600", "the status line counted the frames");

        var live = harness.View.Live;
        Assert.AreEqual(DebugSection.Live, harness.View.Section);
        Assert.AreEqual("frame 1,200 of 3,600", live.ProgressText);
        Assert.AreEqual(0.2f, live.ProgressFraction, 0.0001f);
        Assert.IsTrue(live.ProgressBar!.IsVisible);
        Assert.IsTrue(live.StopButton!.Enabled);
        Assert.AreEqual(harness.Debugging.LaunchedCommandLine, live.LaunchedCommandLineText);
        StringAssert.Contains(live.LaunchedCommandLineText, HeadlessArgument.Argument);

        harness.Debugging.Options = harness.Debugging.Options with { Headless = false };
        harness.Frame();
        StringAssert.Contains(live.LaunchedCommandLineText, HeadlessArgument.Argument);

        harness.Click(live.StopButton);

        Assert.AreEqual(GameLauncher.GameStoppedStatus, harness.Status.LastMessage);
        Assert.IsFalse(harness.Launcher.IsGameRunning);
        Assert.IsFalse(live.StopButton.Enabled);
    }

    [TestMethod]
    public void AWindowedRun_LeavesTheConnectionOnTheStatusLine_AndShowsNoProgress()
    {
        using var harness = new DebugTabHarness(_optionsPath, launchTarget: SleepingGame);
        Launch(harness, DebugLaunchOptions.Default);
        using var link = ConnectedLink(harness);

        FrameUntil(harness, () => harness.Status.LastMessage.StartsWith("Diagnostics: connected (pid", StringComparison.Ordinal), "the connection was shown");

        Assert.IsFalse(harness.View.Live.ProgressBar!.IsVisible);
        Assert.AreEqual(string.Empty, harness.View.Live.ProgressText);
        harness.Launcher.Stop(out _);
    }

    [TestMethod]
    public void ACompletedBenchmark_IsSelectedAndOpenedInReports_WithItsFingerprintOnTheStatusLine()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(600, 602));
        for (var frame = 600; frame < 602; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            benchmark.Record(FrameCostCategory.Update, "SystemManager", "AuraSystem", TimeSpan.FromMilliseconds(0.5));
            benchmark.SimulationFrameEnded(frame);
        }

        var reportPath = benchmark.WriteReport(_directory, randomSeed: 5);
        using var harness = new DebugTabHarness(_optionsPath, [_directory], SleepingGame);
        harness.View.ShowSection(DebugSection.Reports);
        Launch(harness, HeadlessOptions);
        using var link = ConnectedLink(harness);
        link.SendBenchmarkCompleted(new DiagnosticsLinkBenchmarkCompletion(600, 602, reportPath, null, null));

        var reports = harness.View.Reports;
        FrameUntil(harness, () => reports.OpenSession is not null, "the completed benchmark's report opened");

        var fileName = Path.GetFileName(reportPath);
        Assert.AreEqual(DebugSection.Reports, harness.View.Section);
        Assert.AreEqual(fileName, ((DiagnosticsReportEntry)reports.ReportList.SelectedRow!.Tag!).FileName);
        Assert.AreEqual($"Benchmark complete: {fileName}.", harness.Status.LastMessage);

        harness.Debugging.ObserveGameOutput(new GameOutputLine(HeadlessArgument.FormatFingerprintLine("00c0ffee"), IsError: false));
        harness.Frame();

        Assert.AreEqual($"Benchmark complete: {fileName}, fingerprint 00c0ffee.", harness.Status.LastMessage);
        harness.Launcher.Stop(out _);
        harness.Frame();

        Launch(harness, DebugLaunchOptions.Default);
        harness.Frame();

        Assert.IsFalse(harness.Status.LastMessage.StartsWith("Benchmark complete", StringComparison.Ordinal), harness.Status.LastMessage);
        harness.Launcher.Stop(out _);
    }

    [TestMethod]
    public void ACompletedBenchmark_WhileReportsIsNotShown_PointsThere_AndOpensWhenItIsShown()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(600, 601));
        benchmark.SimulationFrameStarting(600);
        benchmark.Record(FrameCostCategory.Update, "SystemManager", "AuraSystem", TimeSpan.FromMilliseconds(0.5));
        benchmark.SimulationFrameEnded(600);
        var reportPath = benchmark.WriteReport(_directory, randomSeed: 5);
        using var harness = new DebugTabHarness(_optionsPath, [_directory], SleepingGame);
        Launch(harness, HeadlessOptions);
        using var link = ConnectedLink(harness);
        link.SendBenchmarkCompleted(new DiagnosticsLinkBenchmarkCompletion(600, 601, reportPath, null, null));

        var fileName = Path.GetFileName(reportPath);
        FrameUntil(harness, () => harness.Status.LastMessage.StartsWith("Benchmark complete", StringComparison.Ordinal), "the completion was shown");

        Assert.AreEqual($"Benchmark complete: {fileName} -- open Debug > Reports", harness.Status.LastMessage);
        Assert.AreEqual(DebugSection.Live, harness.View.Section);
        Assert.IsNull(harness.View.Reports.OpenSession);

        harness.View.ShowSection(DebugSection.Reports);
        FrameUntil(harness, () => harness.View.Reports.OpenSession is not null, "the report opened once Reports was shown");
        harness.Launcher.Stop(out _);
    }

    [TestMethod]
    public void AHeadlessRunTheGameRefuses_SaysItEndedEarly_WithTheGamesOwnErrorLine()
    {
        using var harness = new DebugTabHarness(_optionsPath, launchTarget: CmdTarget($"/c (echo {HeadlessArgument.RequiresRangeMessage})1>&2 & exit 2"));
        Launch(harness, HeadlessOptions);

        FrameUntil(harness, () => harness.Status.LastMessage.StartsWith("Headless benchmark ended early", StringComparison.Ordinal), "the run ended");

        Assert.AreEqual($"Headless benchmark ended early (exit code 2): {HeadlessArgument.RequiresRangeMessage}", harness.Status.LastMessage);
    }

    [TestMethod]
    public void AStoppedHeadlessRun_SaysGameStopped_EvenAfterItsLinkCloses()
    {
        using var harness = new DebugTabHarness(_optionsPath, launchTarget: SleepingGame);
        Launch(harness, HeadlessOptions);
        var link = ConnectedLink(harness);
        FrameUntil(harness, () => harness.Debugging.LaunchedSession?.Hello is not null, "the game connected");

        Assert.IsTrue(harness.Debugging.Stop(out var status));
        link.Dispose();
        FrameUntil(harness, () => harness.Debugging.LaunchedSession!.State == DiagnosticsConnectionState.Ended, "the link closed");

        Assert.AreEqual(GameLauncher.GameStoppedStatus, status);
        Assert.AreEqual(GameLauncher.GameStoppedStatus, harness.Status.LastMessage);
    }

    [TestMethod]
    public void TheFingerprintLine_FormatsAndParsesBack()
    {
        var line = HeadlessArgument.FormatFingerprintLine("1a2b3c");

        Assert.AreEqual("[Headless] Fingerprint 1a2b3c", line);
        Assert.IsTrue(HeadlessArgument.TryParseFingerprintLine(line, out var fingerprint));
        Assert.AreEqual("1a2b3c", fingerprint);
        Assert.IsFalse(HeadlessArgument.TryParseFingerprintLine("[Headless] Something else", out _));
    }
}
