using System.Text.Json;
using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.UI;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DebugTabTests
{
    private string _optionsPath = null!;

    [TestInitialize]
    public void CreateOptionsPath() => _optionsPath = Path.Combine(Path.GetTempPath(), $"debug-launch-options-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void DeleteOptionsFile() => File.Delete(_optionsPath);

    private static readonly DiagnosticsLinkGaugeDescriptor[] TwoGauges =
    [
        new("Process", "HeapBytes", GaugeKind.Level, GaugeUnit.Bytes),
        new("Streamer", "EntitiesSpawned", GaugeKind.Cumulative, GaugeUnit.Count),
    ];

    private static LiveDiagnosticsSession SessionWith(string features, int frameCount, IReadOnlyList<DiagnosticsLinkGaugeDescriptor> gauges)
    {
        var session = new LiveDiagnosticsSession(historyCapacity: 2_000);
        session.Receive(DiagnosticsLinkMessageType.Hello, JsonSerializer.SerializeToUtf8Bytes(new DiagnosticsLinkHello(DiagnosticsLinkProtocol.Version, 4321, 7, features), DiagnosticsLinkProtocol.JsonOptions));
        session.Receive(DiagnosticsLinkMessageType.SessionStarted, JsonSerializer.SerializeToUtf8Bytes(new DiagnosticsLinkSessionStart(gauges), DiagnosticsLinkProtocol.JsonOptions));
        AddFrames(session, 1, frameCount, gauges.Count);
        return session;
    }

    private static void AddFrames(LiveDiagnosticsSession session, long firstFrame, int frameCount, int gaugeCount)
    {
        var payload = new byte[DiagnosticsFrameRecord.ByteCountFor(gaugeCount)];
        var values = new double[gaugeCount];
        for (var frame = firstFrame; frame < firstFrame + frameCount; frame++)
        {
            Array.Fill(values, frame);
            var updateMilliseconds = frame % 100 == 50 ? 40.0 : 2.0;
            DiagnosticsFrameRecord.Write(new DiagnosticsFrameRecord(frame, updateMilliseconds, 60, 60, IsPaused: false, DroppedBefore: 0), values, payload);
            session.Receive(DiagnosticsLinkMessageType.Frame, payload);
        }

        session.ApplyPending();
    }

    private static Point GaugeRowCenter(DiagnosticsGaugeList list, int gaugeIndex) =>
        new((int)list.ContentAbsolutePosition.X + 60, (int)(list.ContentAbsolutePosition.Y + list.RowTopOf(gaugeIndex) - list.ScrollOffset.Y + DebugTabChrome.GaugeListRowHeight / 2));

    [TestMethod]
    public void TheFrameGraph_IsPartOfTheGaugesView_AboveTheGaugeList_AndHiddenUnderAnyOther()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        harness.Session = SessionWith("FrameBudget, Gauges", 120, TwoGauges);
        harness.View.ShowSection(DebugSection.Live);
        harness.Frame();
        var live = harness.View.Live;

        Assert.AreEqual(LiveView.Gauges, live.View);
        Assert.IsTrue(live.Timeline.IsVisible);
        Assert.IsTrue(live.WindowChoice.IsVisible);
        Assert.IsLessThanOrEqualTo(live.WindowChoice.Rectangle.Top, live.TileFor(LiveView.Gauges).Rectangle.Bottom);
        Assert.IsLessThanOrEqualTo(live.Timeline.Rectangle.Top, live.WindowChoice.Rectangle.Bottom);
        Assert.IsLessThanOrEqualTo(live.GaugeList.Rectangle.Top, live.Timeline.Rectangle.Bottom);

        harness.Click(live.TileFor(LiveView.FrameCosts));

        Assert.IsFalse(live.Timeline.IsVisible);
        Assert.IsFalse(live.WindowChoice.IsVisible);
        Assert.IsFalse(live.Freeze.IsVisible);
        Assert.IsFalse(live.GaugeList.IsVisible);
    }

    [TestMethod]
    public void Launch_StartsWithFrameBudgetAndGauges_AndShowsTheWholeCommandLine()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        harness.Frame();

        var launch = harness.View.Launch;
        Assert.AreEqual(DebugSection.Launch, harness.View.Section);
        Assert.IsTrue(launch.ToggleFor(DiagnosticsFeatures.FrameBudget).IsOn);
        Assert.IsTrue(launch.ToggleFor(DiagnosticsFeatures.Gauges).IsOn);
        Assert.IsFalse(launch.ToggleFor(DiagnosticsFeatures.Memory).IsOn);
        Assert.IsFalse(launch.ToggleFor(DiagnosticsFeatures.All).IsOn);
        Assert.AreEqual($"\"{Path.Combine("bin", "Game.exe")}\" --map-size=1024 {harness.Debugging.Link.LaunchArgument} --diagnostics=frame,gauges", launch.CommandLineText);
        Assert.AreEqual(harness.Launcher.CommandLineFor(harness.Debugging.LaunchArguments), launch.CommandLineText);
    }

    [TestMethod]
    public void UntickingGauges_ChangesWhatPlayPasses_AndIsStillUnticked_AfterARestart()
    {
        using (var harness = new DebugTabHarness(_optionsPath))
        {
            harness.Click(harness.View.Launch.ToggleFor(DiagnosticsFeatures.Gauges));

            Assert.IsFalse(harness.View.Launch.ToggleFor(DiagnosticsFeatures.Gauges).IsOn);
            StringAssert.EndsWith(harness.View.Launch.CommandLineText, "--diagnostics=frame");
            StringAssert.EndsWith(harness.Debugging.LaunchArguments, "--diagnostics=frame");
        }

        using var restarted = new DebugTabHarness(_optionsPath);
        restarted.Frame();

        Assert.IsFalse(restarted.View.Launch.ToggleFor(DiagnosticsFeatures.Gauges).IsOn);
        Assert.IsTrue(restarted.View.Launch.ToggleFor(DiagnosticsFeatures.FrameBudget).IsOn);
        Assert.AreEqual(DiagnosticsFeatures.FrameBudget, restarted.Debugging.Options.DiagnosticsFeatures);
    }

    [TestMethod]
    public void All_TicksEveryFeature_AndUntickingItUnticksThem()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;

        harness.Click(launch.ToggleFor(DiagnosticsFeatures.All));
        Assert.IsTrue(launch.ToggleFor(DiagnosticsFeatures.Memory).IsOn);
        Assert.IsTrue(launch.ToggleFor(DiagnosticsFeatures.LeakDetection).IsOn);
        Assert.IsTrue(launch.ToggleFor(DiagnosticsFeatures.Startup).IsOn);
        StringAssert.EndsWith(launch.CommandLineText, "--diagnostics=all");
        Assert.AreEqual(DiagnosticsFeatures.All, DiagnosticsFeaturesParser.Parse(harness.Debugging.LaunchArguments.Split(' ')));

        harness.Click(launch.ToggleFor(DiagnosticsFeatures.Memory));
        Assert.IsFalse(launch.ToggleFor(DiagnosticsFeatures.All).IsOn);

        harness.Click(launch.ToggleFor(DiagnosticsFeatures.All));
        harness.Click(launch.ToggleFor(DiagnosticsFeatures.All));
        Assert.IsFalse(launch.ToggleFor(DiagnosticsFeatures.FrameBudget).IsOn);
        StringAssert.EndsWith(launch.CommandLineText, "--diagnostics=none");
    }

    [TestMethod]
    public void LaunchSettings_IndentsTheFeaturesUnderAll()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        harness.Frame();
        var launch = harness.View.Launch;

        Assert.AreEqual("Launch Settings", launch.Window.TitleText);
        Assert.AreEqual("Launch Settings", harness.View.TileFor(DebugSection.Launch).OriginalText);
        var allLeft = launch.ToggleFor(DiagnosticsFeatures.All).AbsolutePosition.X;
        foreach (var feature in new[] { DiagnosticsFeatures.FrameBudget, DiagnosticsFeatures.Memory, DiagnosticsFeatures.LeakDetection, DiagnosticsFeatures.Startup, DiagnosticsFeatures.Gauges })
        {
            Assert.AreEqual(allLeft + ToggleList.ItemIndent, launch.ToggleFor(feature).AbsolutePosition.X, 0.5f, feature.ToString());
        }
    }

    [TestMethod]
    public void TheCopyButtons_CopyTheCommandLineAndTheSettingsFilesPath()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        Assert.AreEqual(_optionsPath, launch.OptionsFileText);

        harness.Click(launch.CopyCommandLineButton);
        Assert.AreEqual(launch.CommandLineText, harness.Clipboard[^1]);
        Assert.AreEqual("Copied the command line.", harness.Status.LastMessage);

        harness.Click(launch.CopyOptionsFileButton);
        Assert.AreEqual(_optionsPath, harness.Clipboard[^1]);
        Assert.HasCount(2, harness.Clipboard);
    }

    [TestMethod]
    public void TheCommandLine_CannotBeTypedInto()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        var launch = harness.View.Launch;
        var commandLine = launch.CommandLineText;

        harness.Click(launch.CommandLineBox);
        harness.Type("oops");

        Assert.IsTrue(launch.CommandLineBox.IsReadOnly);
        Assert.AreEqual(commandLine, launch.CommandLineText);
    }

    [TestMethod]
    public void TheLiveTile_ShowsLive_WaitingForPlay()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        harness.Click(harness.View.TileFor(DebugSection.Live));

        Assert.AreEqual(DebugSection.Live, harness.View.Section);
        Assert.IsTrue(harness.View.Live.Window.IsVisible);
        Assert.IsFalse(harness.View.Launch.Window.IsVisible);
        StringAssert.StartsWith(harness.View.Live.ConnectionText, "Waiting for Play");
        StringAssert.Contains(harness.View.Live.GaugeList.Message, "Play (F5)");
    }

    [TestMethod]
    public void Live_ShowsTheConnectedGame_AndSelectingAGaugeRowGraphsIt()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("FrameBudget, Gauges", 300, TwoGauges) };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        var live = harness.View.Live;

        Assert.AreEqual("4321", live.ProcessIdBox.OriginalText);
        Assert.IsTrue(live.ProcessIdBox.IsReadOnly);
        Assert.AreEqual("7", live.SeedBox.OriginalText);
        Assert.IsTrue(live.SeedBox.IsReadOnly);
        Assert.AreEqual("Connected", live.ConnectionText);
        var timelineHeight = live.Timeline.CurrentSize.Y;

        harness.Click(GaugeRowCenter(live.GaugeList, 1));

        CollectionAssert.AreEqual(new[] { 1 }, live.GaugeList.SelectedGaugeIndexes.ToArray());
        Assert.IsGreaterThan(timelineHeight, live.Timeline.CurrentSize.Y);

        harness.Click(GaugeRowCenter(live.GaugeList, 1));
        Assert.IsEmpty(live.GaugeList.SelectedGaugeIndexes);
    }

    [TestMethod]
    public void Freeze_StopsTheWindowAdvancing_WhileTheSessionKeepsRecording()
    {
        var session = SessionWith("Gauges", 300, TwoGauges);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        var live = harness.View.Live;
        Assert.AreEqual(300, live.WindowEndRecord);

        harness.Click(live.Freeze);
        AddFrames(session, 301, 120, TwoGauges.Length);
        harness.Frame();

        Assert.AreEqual(420, session.History!.TotalRecordedCount);
        Assert.AreEqual(300, live.WindowEndRecord);

        harness.Click(live.Freeze);
        Assert.AreEqual(420, live.WindowEndRecord);
    }

    [TestMethod]
    public void AClickOnTheGraph_SelectsTheSlowestFrameUnderIt_AndARightClickClearsIt()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("Gauges", 300, TwoGauges) };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        var live = harness.View.Live;
        var timeline = live.Timeline;
        StringAssert.StartsWith(live.ReadoutText, "Click a frame");

        var spikeIndexInWindow = 250 - 1 - (300 - live.WindowFrames);
        var spikeColumn = (int)System.Math.Ceiling((spikeIndexInWindow + 0.5) * (int)timeline.ContentSize.X / live.WindowFrames) - 1;
        harness.Click(new Point((int)System.Math.Ceiling(timeline.ContentAbsolutePosition.X) + spikeColumn, (int)timeline.ContentAbsolutePosition.Y + 20));

        Assert.AreEqual((249L, 249L), timeline.Selection);
        StringAssert.StartsWith(live.ReadoutText, "Frame ");
        StringAssert.Contains(live.ReadoutText, "40.00 ms");
    }

    private static Point TimelinePoint(DiagnosticsTimelineView timeline, int column) =>
        new((int)System.Math.Ceiling(timeline.ContentAbsolutePosition.X) + column, (int)timeline.ContentAbsolutePosition.Y + 20);

    [TestMethod]
    public void ADragAcrossTheGraph_SelectsEveryFrameBetween_AndARightClickClearsIt()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("Gauges", 300, TwoGauges) };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        var live = harness.View.Live;
        var timeline = live.Timeline;
        var width = (int)timeline.ContentSize.X;

        harness.Drag(TimelinePoint(timeline, width - 100), TimelinePoint(timeline, width - 20));

        Assert.IsNotNull(timeline.Selection);
        var (firstRecord, lastRecord) = timeline.Selection.Value;
        Assert.IsGreaterThan(firstRecord, lastRecord);
        Assert.IsTrue(timeline.TryGetSelectionSummary(out var summary));
        Assert.AreEqual(lastRecord - firstRecord + 1, summary.FrameCount);
        StringAssert.StartsWith(live.ReadoutText, $"Frames {summary.FirstFrame:N0}-{summary.LastFrame:N0}");

        harness.RightClick(TimelinePoint(timeline, width / 2));

        Assert.IsNull(timeline.Selection);
        StringAssert.StartsWith(live.ReadoutText, "Click a frame");
    }

    [TestMethod]
    public void Space_FreezesAndUnfreezesTheLiveWindow()
    {
        var session = SessionWith("Gauges", 300, TwoGauges);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        var live = harness.View.Live;

        harness.Press(Microsoft.Xna.Framework.Input.Keys.Space);
        AddFrames(session, 301, 60, TwoGauges.Length);
        harness.Frame();

        Assert.IsTrue(live.Freeze.IsOn);
        Assert.AreEqual(300, live.WindowEndRecord);

        harness.Press(Microsoft.Xna.Framework.Input.Keys.Space);

        Assert.IsFalse(live.Freeze.IsOn);
        Assert.AreEqual(360, live.WindowEndRecord);
    }

    [TestMethod]
    public void Space_WhileLaunchSettingsIsShown_DoesNotFreeze()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("Gauges", 10, TwoGauges) };

        harness.Press(Microsoft.Xna.Framework.Input.Keys.Space);

        Assert.IsFalse(harness.View.Live.Freeze.IsOn);
    }

    [TestMethod]
    public void ClickingAFrameCost_HighlightsWhatRanInsideIt_LighterAtEachLayer()
    {
        FrameCostRowName frame = new(FrameCostCategory.Update, "GameLoop", "EcsContext.Update (all systems)");
        FrameCostRowName auraSystem = new(FrameCostCategory.Update, "SystemManager", "AuraSystem");
        FrameCostEntry[] costs =
        [
            new(FrameCostCategory.Update, "GameLoop", "EcsContext.Update (all systems)", 100),
            new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 40, [frame]),
            new(FrameCostCategory.Update, "SystemManager", "MovementSystem", 30, [frame]),
            new(FrameCostCategory.Update, "EventBus", "AuraSourceRemovedEvent", 10, [auraSystem]),
            new(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", 50),
        ];
        var session = SessionWith("FrameBudget", 10, []);
        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, costs);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        harness.Click(harness.View.Live.TileFor(LiveView.FrameCosts));
        var list = harness.View.Live.ReportViews.ListFor(LiveView.FrameCosts);
        ListViewRow RowOf(string item) => list.RowsInDisplayOrder.First(row => row.Cells[2] == item);

        list.Select(RowOf("EcsContext.Update (all systems)"));

        Assert.AreEqual(LiveReportViews.ChildHighlightColor(1), list.RowColorOf(RowOf("AuraSystem")));
        Assert.AreEqual(LiveReportViews.ChildHighlightColor(1), list.RowColorOf(RowOf("MovementSystem")));
        Assert.AreEqual(LiveReportViews.ChildHighlightColor(2), list.RowColorOf(RowOf("AuraSourceRemovedEvent")));
        Assert.IsNull(list.RowColorOf(RowOf("Shell.Draw")));
        Assert.IsLessThan(LiveReportViews.ChildHighlightColor(1).A, LiveReportViews.ChildHighlightColor(2).A);

        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, costs);
        harness.Frame();

        Assert.AreEqual("EcsContext.Update (all systems)", list.SelectedRow?.Cells[2]);
        Assert.AreEqual(LiveReportViews.ChildHighlightColor(2), list.RowColorOf(RowOf("AuraSourceRemovedEvent")));
    }

    [TestMethod]
    public void ASelectedFrameCost_MissingFromOneReport_IsSelectedAgainWhenItReturns()
    {
        FrameCostEntry[] withEvent = [new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 40), new(FrameCostCategory.Update, "EventBus", "LevelUpEvent", 5)];
        FrameCostEntry[] withoutEvent = [new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 40)];
        var session = SessionWith("FrameBudget", 10, []);
        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, withEvent);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));
        harness.Click(harness.View.Live.TileFor(LiveView.FrameCosts));
        var list = harness.View.Live.ReportViews.ListFor(LiveView.FrameCosts);
        list.Select(list.RowsInDisplayOrder.First(row => row.Cells[2] == "LevelUpEvent"));

        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, withoutEvent);
        harness.Frame();
        Assert.IsNull(list.SelectedRow);

        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, withEvent);
        harness.Frame();
        Assert.AreEqual("LevelUpEvent", list.SelectedRow?.Cells[2]);
    }

    [TestMethod]
    public void AGameRunWithoutGauges_SaysWhichCheckboxTurnsThemOn()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("FrameBudget", 10, []) };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        StringAssert.Contains(harness.View.Live.GaugeList.Message, "tick Gauges under Launch");
    }

    private static void ReceiveReport(LiveDiagnosticsSession session, DiagnosticsFeatures features, FrameCostEntry[]? frameCosts = null, ComponentMemoryEntry[]? memory = null, LeakFinding[]? leaks = null)
    {
        var report = DiagnosticsReportWriter.Build(DateTime.UtcNow, features, 7, frameCosts, memory, leaks, null);
        session.Receive(DiagnosticsLinkMessageType.PeriodicReport, System.Text.Encoding.UTF8.GetBytes(DiagnosticsReportWriter.Serialize(report)));
        session.ApplyPending();
    }

    [TestMethod]
    public void FrameCosts_AreSortedCostliestFirst_WithTheirChangeSinceThePreviousReport()
    {
        var session = SessionWith("FrameBudget, Gauges", 10, TwoGauges);
        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, [new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 20), new(FrameCostCategory.Update, "SystemManager", "MovementSystem", 30)]);
        ReceiveReport(session, DiagnosticsFeatures.FrameBudget, [new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 24.5), new(FrameCostCategory.Update, "SystemManager", "MovementSystem", 10), new(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", 12)]);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        harness.Click(harness.View.Live.TileFor(LiveView.FrameCosts));

        var list = harness.View.Live.ReportViews.ListFor(LiveView.FrameCosts);
        Assert.IsTrue(list.IsVisible);
        Assert.IsFalse(harness.View.Live.GaugeList.IsVisible);
        var rows = list.RowsInDisplayOrder.Select(row => row.Cells).ToArray();
        CollectionAssert.AreEqual(new[] { "Update", "SystemManager", "AuraSystem", "24.5", "+4.5" }, rows[0].ToArray());
        CollectionAssert.AreEqual(new[] { "Draw", "GameLoop", "Shell.Draw", "12.0", "new" }, rows[1].ToArray());
        CollectionAssert.AreEqual(new[] { "Update", "SystemManager", "MovementSystem", "10.0", "-20.0" }, rows[2].ToArray());
        StringAssert.Contains(harness.View.Live.ViewMessageText, "Milliseconds spent per second");
    }

    [TestMethod]
    public void Memory_ListsEveryPool_WithBytesPerHolderAndItsPopulation()
    {
        var session = SessionWith("Memory", 10, []);
        ReceiveReport(session, DiagnosticsFeatures.Memory, memory: [new("HealthComponent", 1_000, 96_000, "Built"), new("TransformComponent", 4_000, 4 * 1024 * 1024, "Every entity")]);
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        harness.Click(harness.View.Live.TileFor(LiveView.Memory));

        var rows = harness.View.Live.ReportViews.ListFor(LiveView.Memory).RowsInDisplayOrder.Select(row => row.Cells).ToArray();
        CollectionAssert.AreEqual(new[] { "TransformComponent", "4000", "4.00", "1049", "Every entity" }, rows[0].ToArray());
        CollectionAssert.AreEqual(new[] { "HealthComponent", "1000", "0.09", "96", "Built" }, rows[1].ToArray());
    }

    [TestMethod]
    public void Startup_IsTheScopeTree_EachPhaseIndentedUnderTheOneItRanInside()
    {
        var session = SessionWith("Startup", 10, []);
        var startup = new DiagnosticsStartupReport(DateTime.UtcNow, true, 4321, [new PhaseRecord("World Session Setup", 900, 0), new PhaseRecord("Configure", 40, 1), new PhaseRecord("Configure (Health)", 3, 2)]);
        session.Receive(DiagnosticsLinkMessageType.StartupReport, JsonSerializer.SerializeToUtf8Bytes(startup));
        session.ApplyPending();
        using var harness = new DebugTabHarness(_optionsPath) { Session = session };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        harness.Click(harness.View.Live.TileFor(LiveView.Startup));

        var phases = harness.View.Live.ReportViews.ListFor(LiveView.Startup).RowsInDisplayOrder.Select(row => row.Cells[0]).ToArray();
        CollectionAssert.AreEqual(new[] { "World Session Setup", "    Configure", "        Configure (Health)" }, phases);
        StringAssert.Contains(harness.View.Live.ViewMessageText, "4,321 ms");
    }

    [TestMethod]
    public void AReportViewWhoseFeatureIsOff_SaysWhichCheckboxTurnsItOn()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("Gauges", 10, TwoGauges) };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        harness.Click(harness.View.Live.TileFor(LiveView.Leaks));

        Assert.AreEqual("Leaks is off for this run: tick Leaks under Launch Settings, then Play again.", harness.View.Live.ViewMessageText);
        Assert.IsFalse(harness.View.Live.ReportViews.ListFor(LiveView.Leaks).IsVisible);
    }

    [TestMethod]
    public void ALaunchFromLaunchSettings_ShowsLive()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        Assert.AreEqual(DebugSection.Launch, harness.View.Section);

        harness.Debugging.NotifyGameLaunched();

        Assert.AreEqual(DebugSection.Live, harness.View.Section);
        Assert.IsTrue(harness.View.Live.Window.IsVisible);
    }

    [TestMethod]
    public void ALaunch_WhileTheTabIsClosed_ChangesNothingWhenItReopens()
    {
        using var harness = new DebugTabHarness(_optionsPath);
        harness.View.Close();

        harness.Debugging.NotifyGameLaunched();
        harness.View.Open(new Rectangle(0, 30, 1600, 870));

        Assert.AreEqual(DebugSection.Launch, harness.View.Section);
    }

    [TestMethod]
    public void EverySparkline_FillsItsWidth_EvenFromAWindowWithFewerFrames()
    {
        using var harness = new DebugTabHarness(_optionsPath) { Session = SessionWith("Gauges", 300, TwoGauges) };
        harness.Click(harness.View.TileFor(DebugSection.Live));

        Assert.AreEqual(DebugTabChrome.GaugeSparklineWidth, harness.View.Live.GaugeList.SparklinePointCount);
    }

    [TestMethod]
    public void LeakFindings_BecomeWarnings_NamingTheirPool()
    {
        var report = DiagnosticsReportWriter.Build(DateTime.UtcNow, DiagnosticsFeatures.LeakDetection, 7, null, null, [new LeakFinding("HealthComponent", "Grew 40% beyond its population.", 0.4)], null);

        var problem = DiagnosticsProblems.LeaksOf(report).Single();

        Assert.AreEqual(WorkshopProblemSeverity.Warning, problem.Severity);
        Assert.AreEqual("Possible leak in HealthComponent: Grew 40% beyond its population.", problem.Description);
        Assert.AreEqual(DiagnosticsProblems.Source, problem.Source);
        Assert.IsNull(problem.RecordId);
        Assert.IsEmpty(DiagnosticsProblems.LeaksOf(null));
    }

    [TestMethod]
    public void TheHistory_ReadsAWindowOldestFirst_ClampedToWhatItHolds()
    {
        var history = new DiagnosticsFrameHistory(gaugeCount: 0, capacity: 4);
        for (var frame = 1; frame <= 6; frame++)
        {
            history.Record(frame, frame * 10, []);
        }

        var values = new double[10];
        Assert.AreEqual(3, history.CopyWindow(0, endRecord: 6, count: 3, values));
        CollectionAssert.AreEqual(new[] { 40.0, 50.0, 60.0 }, values[..3]);

        Assert.AreEqual(3, history.CopyWindow(0, endRecord: 5, count: 10, values));
        CollectionAssert.AreEqual(new[] { 30.0, 40.0, 50.0 }, values[..3]);

        var frames = new long[10];
        Assert.AreEqual(2, history.CopyFrameWindow(endRecord: 6, count: 2, frames));
        CollectionAssert.AreEqual(new long[] { 5, 6 }, frames[..2]);
    }

    [TestMethod]
    public void TheHistory_KeepsTenMinutesAtSixtyFramesPerSecond()
    {
        Assert.AreEqual(10 * 60 * 60, DiagnosticsFrameHistory.DefaultCapacity);
    }

    [TestMethod]
    public void AMissingOrUnreadableOptionsFile_ReadsAsTheDefaults()
    {
        var store = new DebugLaunchOptionsStore(_optionsPath);
        Assert.AreEqual(DebugLaunchOptions.Default, store.Load());

        File.WriteAllText(_optionsPath, "{ not json");
        Assert.AreEqual(DebugLaunchOptions.Default, store.Load());
    }
}
