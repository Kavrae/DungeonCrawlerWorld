using System.Text;
using Engine.Diagnostics;
using UIEngine.UI;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DebugReportsTests
{
    private const string SavedRunFileName = "20261024-120000.json";
    private const string AbFileName = "ab-20261024-130000.json";
    private const string OlderMemoryFileName = "memory-20261023-110000-41.json";
    private const string OlderStartupFileName = "startup-20261023-100000.json";

    private string _directory = null!;
    private string _optionsPath = null!;

    [TestInitialize]
    public void WriteReports()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"debug-reports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _optionsPath = Path.Combine(_directory, "options.json");

        DiagnosticsReportWriter.Write(_directory, DiagnosticsFeatures.FrameBudget, 1, [new FrameCostEntry(FrameCostCategory.Update, "SystemManager", "AuraSystem", 30)], null, null, null);

        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(600, 602));
        for (var frame = 600; frame < 602; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            benchmark.Record(FrameCostCategory.Update, "SystemManager", "AuraSystem", TimeSpan.FromMilliseconds(0.5));
            benchmark.Record(FrameCostCategory.Update, "SystemManager", "MovementSystem", TimeSpan.FromMilliseconds(0.2));
            benchmark.SimulationFrameEnded(frame);
        }

        benchmark.WriteReport(_directory, randomSeed: 1);

        var reading = 0.0;
        var tracker = new GaugeTracker([new Gauge("Streamer", "QueuedBytes", GaugeKind.Level, () => reading)]);
        var series = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(600, 610));
        for (var frame = 600; frame < 610; frame++)
        {
            reading = frame;
            tracker.Sample();
            series.Record(frame, TimeSpan.FromMilliseconds(2));
        }

        series.WriteReport(_directory, randomSeed: 1);

        var startup = new StartupProfiler();
        startup.ScopeStarted(new DiagnosticScope("World Session Setup"));
        startup.ScopeEnded(new DiagnosticScope("World Session Setup"), TimeSpan.FromMilliseconds(900));
        startup.WriteReport(_directory);

        File.WriteAllText(Path.Combine(_directory, SavedRunFileName), """
            {
                "timestampUtc":  "2026-10-24T12:00:00Z",
                "sampling":  "frame-range-headless",
                "configuration":  "Release",
                "randomSeed":  1,
                "mapSize":  3072,
                "startFrame":  600,
                "endFrame":  602,
                "runs":  5,
                "gitCommit":  "abc1234",
                "systems":  { "Update.SystemManager.AuraSystem":  0.25, "Update.SystemManager.MovementSystem":  0.2, "Update.SystemManager.GoneSystem":  0.1 }
            }
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        File.WriteAllText(Path.Combine(_directory, AbFileName), """
            {
                "timestampUtc":  "2026-10-24T13:00:00Z",
                "sampling":  "ab-headless",
                "configuration":  "Release",
                "randomSeed":  1,
                "startFrame":  600,
                "endFrame":  3600,
                "baseline":  { "gitCommit":  "d8564b2" },
                "currentCommit":  "4f84efd",
                "systems":  { "Update.SystemManager.AuraSystem":  { "a":  0.3909, "b":  0.3751, "deltaPercent":  -4.0 } }
            }
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        File.WriteAllText(Path.Combine(_directory, "memory-20261024-110000-42.json"), System.Text.Json.JsonSerializer.Serialize(new DiagnosticsMemoryReport(
            new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc), 1, 42, 600, 3600, 1000, 0, 0, 0, 0, 0, 0, 2048,
            [new PoolMemoryReportEntry("HealthComponent", "Packed", 16, false, 100, 2048, 100, 100, 100, 5)])));

        File.WriteAllText(Path.Combine(_directory, OlderMemoryFileName), System.Text.Json.JsonSerializer.Serialize(new DiagnosticsMemoryReport(
            new DateTime(2026, 10, 23, 11, 0, 0, DateTimeKind.Utc), 1, 41, 600, 3600, 1000, 0, 0, 0, 0, 0, 0, 1024,
            [new PoolMemoryReportEntry("HealthComponent", "Packed", 16, false, 50, 1024, 50, 50, 50, 5), new PoolMemoryReportEntry("GoneComponent", "Packed", 8, false, 10, 512 * 1024, 10, 10, 10, 0)])));

        File.WriteAllText(Path.Combine(_directory, OlderStartupFileName), System.Text.Json.JsonSerializer.Serialize(new DiagnosticsStartupReport(
            new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc), true, 5000, [new PhaseRecord("World Session Setup", 600, 0), new PhaseRecord("Configure", 40, 1)])));

        File.WriteAllText(Path.Combine(_directory, "notes.json"), "{}");
    }

    [TestCleanup]
    public void DeleteReports() => Directory.Delete(_directory, recursive: true);

    private DebugTabHarness OpenReports()
    {
        var harness = new DebugTabHarness(_optionsPath, [_directory]);
        harness.Click(harness.View.TileFor(DebugSection.Reports));
        harness.View.Reports.WaitForScan();
        harness.Frame();
        return harness;
    }

    private static DiagnosticsReportEntry EntryOf(DebugReportsSection reports, DiagnosticsReportKind kind) => reports.Entries.First(entry => entry.Kind == kind);

    private static void Select(DebugReportsSection reports, DiagnosticsReportKind kind) =>
        reports.ReportList.Select(reports.ReportList.RowsInDisplayOrder.First(row => ((DiagnosticsReportEntry)row.Tag!).Kind == kind));

    [TestMethod]
    public void TheBrowser_ListsEveryKnownReport_NewestFirst_WithItsSeedAndFrames()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        CollectionAssert.AreEquivalent(Enum.GetValues<DiagnosticsReportKind>(), reports.Entries.Select(static entry => entry.Kind).Distinct().ToArray());
        Assert.IsFalse(reports.Entries.Any(static entry => entry.FileName == "notes.json"));
        CollectionAssert.AreEqual(reports.Entries.OrderByDescending(static entry => entry.TimestampUtc).ToArray(), reports.Entries.ToArray());
        var savedRun = EntryOf(reports, DiagnosticsReportKind.SavedRun);
        Assert.AreEqual(1, savedRun.Seed);
        Assert.AreEqual(600, savedRun.StartFrame);
        Assert.AreEqual(602, savedRun.EndFrame);
        StringAssert.Contains(savedRun.Detail, "commit abc1234");
        Assert.AreEqual(reports.Entries.Count, reports.ReportList.RowsInDisplayOrder.Count());
    }

    [TestMethod]
    public void ABenchmark_OpensInFrameCosts_PerFrame()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        Select(reports, DiagnosticsReportKind.Benchmark);

        harness.Click(reports.OpenButton);
        harness.Click(reports.TabTileFor(LiveView.FrameCosts));

        Assert.IsFalse(reports.ShowsComparison);
        var list = reports.Viewer.ReportViews.ListFor(LiveView.FrameCosts);
        var aura = list.RowsInDisplayOrder.First(static row => row.Cells[2] == "AuraSystem");
        Assert.AreEqual("0.5000", aura.Cells[3]);
        StringAssert.StartsWith(reports.Viewer.Window.TitleText, "Report: benchmark-");
        Assert.AreEqual(string.Empty, reports.Viewer.ConnectionText);
        Assert.AreEqual("Milliseconds per frame.", reports.Viewer.ViewMessageText);

        harness.Click(reports.TabTileFor(LiveView.Startup));

        Assert.AreEqual("No startup report available.", reports.Viewer.ViewMessageText);
        harness.Click(reports.TabTileFor(LiveView.Gauges));
        Assert.AreEqual("No gauge report available.", reports.Viewer.GaugeList.Message);
    }

    [TestMethod]
    public void AGaugeSeries_OpensAsTheFrameGraphAndItsGauges()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        reports.OpenEntry(EntryOf(reports, DiagnosticsReportKind.Gauges));
        harness.Frame();

        var history = reports.OpenSession!.History!;
        Assert.AreEqual(10, history.RecordedFrameCount);
        Assert.AreEqual(609, history.LatestFrame);
        Assert.AreEqual(609, history.GetLatestValue(1));
        Assert.AreEqual("QueuedBytes", reports.OpenSession.Gauges.Single().Name);
        Assert.AreEqual(GaugeUnit.Bytes, reports.OpenSession.Gauges.Single().Unit);
    }

    [TestMethod]
    public void EveryKind_Opens()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        foreach (var entry in reports.Entries)
        {
            reports.OpenEntry(entry);
            harness.Frame();
            StringAssert.StartsWith(harness.Status.LastMessage, entry.Kind == DiagnosticsReportKind.AbComparison ? harness.Status.LastMessage : "Opened ", entry.FileName);
        }

        reports.OpenEntry(EntryOf(reports, DiagnosticsReportKind.Startup));
        harness.Frame();
        Assert.AreEqual("World Session Setup", reports.OpenSession!.StartupReport!.Phases.Single().Name);
    }

    [TestMethod]
    public void ARunComparedWithABaseline_ListsEveryItem_LargestChangeFirst_FlaggingThePastThreshold()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        Select(reports, DiagnosticsReportKind.SavedRun);
        harness.Click(reports.UseAsBaselineButton);
        Select(reports, DiagnosticsReportKind.Benchmark);

        harness.Click(reports.CompareButton);

        Assert.IsTrue(reports.ShowsComparison);
        var rows = reports.ComparisonListFor(LiveView.FrameCosts).RowsInDisplayOrder.Select(static row => (BenchmarkComparisonRow)row.Tag!).ToArray();
        CollectionAssert.AreEqual(new[] { "Update.SystemManager.AuraSystem", "Update.SystemManager.GoneSystem", "Update.SystemManager.MovementSystem" }, rows.Select(static row => row.Name).ToArray());
        Assert.AreEqual(BenchmarkComparisonFlag.Regression, rows[0].Flag);
        Assert.AreEqual(100.0, rows[0].ChangePercent);
        Assert.AreEqual(BenchmarkComparisonFlag.Improvement, rows[1].Flag);
        Assert.AreEqual(BenchmarkComparisonFlag.None, rows[2].Flag);
        var frameCosts = reports.ComparisonListFor(LiveView.FrameCosts);
        Assert.IsNotNull(frameCosts.RowColorOf(frameCosts.RowsInDisplayOrder.First()));
        StringAssert.Contains(reports.ComparisonSummary, "Frame costs: 1 up, 1 down");
    }

    [TestMethod]
    public void AnAB_OpensAsTheComparisonItRecorded()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        reports.OpenEntry(EntryOf(reports, DiagnosticsReportKind.AbComparison));

        Assert.IsTrue(reports.ShowsComparison);
        var row = (BenchmarkComparisonRow)reports.ComparisonListFor(LiveView.FrameCosts).RowsInDisplayOrder.Single().Tag!;
        Assert.AreEqual(0.3909, row.Previous);
        Assert.AreEqual(0.3751, row.Current);
        Assert.AreEqual(-4.0, row.ChangePercent);
    }

    [TestMethod]
    public void CompareWithNoBaseline_SaysToChooseOne()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        Select(reports, DiagnosticsReportKind.Benchmark);

        harness.Click(reports.CompareButton);

        Assert.IsFalse(reports.ShowsComparison);
        StringAssert.StartsWith(harness.Status.LastMessage, "Choose a baseline first");
    }

    private static ListViewRow RowOf(DebugReportsSection reports, string fileName) =>
        reports.ReportList.RowsInDisplayOrder.First(row => ((DiagnosticsReportEntry)row.Tag!).FileName == fileName);

    private static ListViewRow RowOf(DebugReportsSection reports, DiagnosticsReportKind kind) =>
        reports.ReportList.RowsInDisplayOrder.First(row => ((DiagnosticsReportEntry)row.Tag!).Kind == kind);

    [TestMethod]
    public void ARightClickOnARow_SelectsItAndOpensItsMenu()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        var row = RowOf(reports, DiagnosticsReportKind.Startup);
        reports.ReportList.ScrollTo(Microsoft.Xna.Framework.Vector2.Zero);
        var displayIndex = reports.ReportList.RowsInDisplayOrder.ToList().IndexOf(row);
        var position = new Microsoft.Xna.Framework.Point(
            (int)reports.ReportList.ContentAbsolutePosition.X + 40,
            (int)(reports.ReportList.ContentAbsolutePosition.Y + ListView.HeaderHeight + displayIndex * ListView.RowHeight + ListView.RowHeight / 2));

        harness.RightClick(position);

        Assert.AreSame(row, reports.ReportList.SelectedRow);
        Assert.IsTrue(harness.ContextMenus.IsOpen);
        var options = reports.MenuOptionsFor((DiagnosticsReportEntry)row.Tag!);
        CollectionAssert.AreEqual(new[] { "Open", "Use as baseline", "Compare with baseline" }, options.Select(static option => option.Label).ToArray());
        Assert.IsTrue(options[0].Enabled);
        Assert.IsTrue(options[1].Enabled);
        Assert.IsFalse(options[2].Enabled, "No baseline yet.");
    }

    [TestMethod]
    public void TheBaseline_ShowsGreen_AndCannotBeComparedWithItself()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        var baselineRow = RowOf(reports, DiagnosticsReportKind.SavedRun);
        var baseline = (DiagnosticsReportEntry)baselineRow.Tag!;

        reports.UseAsBaseline(baseline);

        Assert.AreEqual(DebugReportsSection.BaselineRowColor, reports.ReportList.RowColorOf(baselineRow));
        Assert.AreEqual(DebugReportsSection.BaselineButtonColor, reports.UseAsBaselineButton.ContentColor);
        var options = reports.MenuOptionsFor(baseline);
        Assert.IsFalse(options[1].Enabled);
        Assert.IsFalse(options[2].Enabled);

        reports.CompareWithBaseline(baseline);

        Assert.IsFalse(reports.ShowsComparison);
        StringAssert.Contains(harness.Status.LastMessage, "is the baseline");
    }

    [TestMethod]
    public void TheComparedReport_ShowsYellow()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.UseAsBaseline((DiagnosticsReportEntry)RowOf(reports, DiagnosticsReportKind.SavedRun).Tag!);
        var comparedRow = RowOf(reports, DiagnosticsReportKind.Benchmark);

        reports.MenuOptionsFor((DiagnosticsReportEntry)comparedRow.Tag!)[2].OnSelect();

        Assert.IsTrue(reports.ShowsComparison);
        comparedRow = RowOf(reports, DiagnosticsReportKind.Benchmark);
        Assert.AreEqual(DebugReportsSection.ComparedRowColor, reports.ReportList.RowColorOf(comparedRow));
        Assert.AreEqual(DebugReportsSection.ComparedButtonColor, reports.CompareButton.ContentColor);
        Assert.AreEqual(DebugReportsSection.BaselineRowColor, reports.ReportList.RowColorOf(RowOf(reports, DiagnosticsReportKind.SavedRun)));
        Assert.AreEqual($"Compare: {((DiagnosticsReportEntry)comparedRow.Tag!).FileName} with {SavedRunFileName}", reports.ComparisonWindow.TitleText);
        Assert.DoesNotContain("Current:", reports.ComparisonSummary);
    }

    [TestMethod]
    public void ATab_ListsOnlyTheReportsHoldingIt()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        harness.Click(reports.TabTileFor(LiveView.Memory));

        Assert.AreEqual(LiveView.Memory, reports.SelectedTab);
        var listed = reports.ReportList.RowsInDisplayOrder.Select(static row => ((DiagnosticsReportEntry)row.Tag!).Kind).ToArray();
        CollectionAssert.AreEqual(new[] { DiagnosticsReportKind.Memory, DiagnosticsReportKind.Memory }, listed);

        harness.Click(reports.TabTileFor(LiveView.FrameCosts));

        CollectionAssert.AreEquivalent(
            new[] { DiagnosticsReportKind.Periodic, DiagnosticsReportKind.Benchmark, DiagnosticsReportKind.SavedRun, DiagnosticsReportKind.AbComparison },
            reports.ReportList.RowsInDisplayOrder.Select(static row => ((DiagnosticsReportEntry)row.Tag!).Kind).ToArray());

        harness.Click(reports.TabTileFor(null));

        Assert.AreEqual(reports.Entries.Count, reports.ReportList.RowsInDisplayOrder.Count());
    }

    [TestMethod]
    public void TheAllTab_ShowsEveryComparedView_InTurn_ATabOnlyItsOwn_AndGaugesANote()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.UseAsBaseline(EntryOf(reports, DiagnosticsReportKind.SavedRun));
        reports.CompareWithBaseline(EntryOf(reports, DiagnosticsReportKind.Benchmark));
        harness.Frame();

        Assert.IsNull(reports.SelectedTab);
        Assert.IsTrue(reports.ShowsComparison);
        Assert.IsTrue(reports.ComparisonWindow.IsVisible);
        Assert.IsFalse(reports.Viewer.Window.IsVisible);
        Assert.IsTrue(reports.ComparisonWindow.CanUserScrollVertical);
        foreach (var view in ReportComparison.Views)
        {
            StringAssert.StartsWith(reports.ComparisonMessageFor(view), view == LiveView.FrameCosts ? "Frame costs in" : string.Empty);
            Assert.AreNotEqual(string.Empty, reports.ComparisonMessageFor(view));
        }

        var frameCosts = reports.ComparisonListFor(LiveView.FrameCosts);
        Assert.HasCount(3, frameCosts.RowsInDisplayOrder.ToList());
        Assert.IsFalse(frameCosts.CanUserScrollVertical, "Under All each list is as tall as its rows; the window scrolls.");
        Assert.AreEqual(ListView.HeaderHeight + 3 * ListView.RowHeight, frameCosts.ContentSize.Y, 0.5f);
        Assert.AreEqual(string.Empty, reports.ComparisonNote);

        harness.Click(reports.TabTileFor(LiveView.FrameCosts));

        Assert.IsTrue(reports.ShowsComparison);
        Assert.AreNotEqual(string.Empty, reports.ComparisonMessageFor(LiveView.FrameCosts));
        Assert.AreEqual(string.Empty, reports.ComparisonMessageFor(LiveView.Memory));
        Assert.IsTrue(frameCosts.CanUserScrollVertical);
        Assert.IsFalse(reports.ComparisonWindow.CanUserScrollVertical);

        harness.Click(reports.TabTileFor(LiveView.Gauges));

        Assert.AreEqual(DebugReportsSection.GaugesNotComparedText, reports.ComparisonNote);
        Assert.IsTrue(ReportComparison.Views.All(view => reports.ComparisonMessageFor(view).Length == 0));
        Assert.IsFalse(frameCosts.IsVisible);
    }

    [TestMethod]
    public void TheTabRow_SpansThePage_OverTheBrowserOnTheLeft_AndTheReportOnTheRight()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;

        var allTile = reports.TabTileFor(null).Rectangle;
        var gaugesTile = reports.TabTileFor(LiveView.Gauges).Rectangle;
        Assert.AreEqual(allTile.Right, gaugesTile.Left);
        Assert.AreEqual(allTile.Top, gaugesTile.Top);
        Assert.IsLessThanOrEqualTo(reports.Browser.Rectangle.Top, allTile.Bottom);
        Assert.IsLessThanOrEqualTo(reports.Viewer.Window.Rectangle.Top, allTile.Bottom);
        Assert.IsLessThanOrEqualTo(reports.Viewer.Window.Rectangle.Left, reports.Browser.Rectangle.Right);
        Assert.AreEqual(reports.Browser.Rectangle.Top, reports.Viewer.Window.Rectangle.Top);
        Assert.IsLessThanOrEqualTo(reports.ReportList.Rectangle.Top, reports.OpenButton.Rectangle.Bottom);
        Assert.IsTrue(reports.ReportList.RowsInDisplayOrder.All(static row => row.Cells.Count == 3), "Kind, date and detail only.");
    }

    [TestMethod]
    public void TheGaugesTab_ListsOnlyTheGaugeSeries_AndShowsOnlyAnOpenReportsGauges()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.OpenEntry(EntryOf(reports, DiagnosticsReportKind.Gauges));

        harness.Click(reports.TabTileFor(LiveView.Gauges));

        CollectionAssert.AreEqual(new[] { DiagnosticsReportKind.Gauges }, reports.ReportList.RowsInDisplayOrder.Select(static row => ((DiagnosticsReportEntry)row.Tag!).Kind).ToArray());
        CollectionAssert.AreEqual(new[] { LiveView.Gauges }, reports.Viewer.ShownViews.ToArray());
        Assert.IsTrue(reports.Viewer.Timeline.IsVisible);
        Assert.IsTrue(reports.Viewer.GaugeList.IsVisible);
    }

    [TestMethod]
    public void AnOpenReport_UnderAll_ShowsEveryViewItHolds_InTurn_AndATabShowsOnlyIts()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        var benchmark = EntryOf(reports, DiagnosticsReportKind.Benchmark);

        reports.OpenEntry(benchmark);
        harness.Frame();

        Assert.IsNull(reports.SelectedTab);
        CollectionAssert.AreEqual(Enum.GetValues<LiveView>().Where(benchmark.Holds).ToArray(), reports.Viewer.ShownViews.ToArray());
        Assert.IsTrue(reports.Viewer.Window.CanUserScrollVertical);
        StringAssert.StartsWith(reports.Viewer.MessageTextFor(LiveView.FrameCosts), "Frame costs: Milliseconds per frame");
        var frameCosts = reports.Viewer.ReportViews.ListFor(LiveView.FrameCosts);
        Assert.IsTrue(frameCosts.IsVisible);
        Assert.IsFalse(frameCosts.CanUserScrollVertical);
        Assert.AreEqual(ListView.HeaderHeight + frameCosts.RowsInDisplayOrder.Count() * ListView.RowHeight, frameCosts.ContentSize.Y, 0.5f, "Every row fits inside the list's border.");
        Assert.IsFalse(reports.Viewer.Timeline.IsVisible, "A benchmark holds no gauges.");

        harness.Click(reports.TabTileFor(LiveView.Memory));

        CollectionAssert.AreEqual(new[] { LiveView.Memory }, reports.Viewer.ShownViews.ToArray());
        Assert.IsFalse(reports.Viewer.Window.CanUserScrollVertical);
        Assert.IsFalse(frameCosts.IsVisible);
    }

    [TestMethod]
    public void Refresh_ClearsTheBaselineTheComparedReportAndTheSelection()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.UseAsBaseline(EntryOf(reports, DiagnosticsReportKind.SavedRun));
        reports.CompareWithBaseline(EntryOf(reports, DiagnosticsReportKind.Benchmark));
        Select(reports, DiagnosticsReportKind.Benchmark);

        reports.StartScan();
        reports.WaitForScan();

        Assert.IsNull(reports.Baseline);
        Assert.IsNull(reports.Compared);
        Assert.IsNull(reports.Comparison);
        Assert.IsFalse(reports.ShowsComparison);
        Assert.IsNull(reports.ReportList.SelectedRow);
        Assert.AreEqual("Compare", reports.ComparisonWindow.TitleText);
        Assert.IsFalse(reports.ReportList.RowsInDisplayOrder.Any(row => reports.ReportList.RowColorOf(row) is not null));
    }

    [TestMethod]
    public void TwoMemoryReports_CompareInTheMemoryTab()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.UseAsBaseline((DiagnosticsReportEntry)RowOf(reports, OlderMemoryFileName).Tag!);

        reports.CompareWithBaseline(EntryOf(reports, DiagnosticsReportKind.Memory));

        harness.Click(reports.TabTileFor(LiveView.Memory));

        var rows = reports.ComparisonListFor(LiveView.Memory).RowsInDisplayOrder.Select(static row => (BenchmarkComparisonRow)row.Tag!).ToDictionary(static row => row.Name);
        Assert.AreEqual(2048 / (1024.0 * 1024.0), rows["HealthComponent"].Current, 1e-12);
        Assert.AreEqual(1024 / (1024.0 * 1024.0), rows["HealthComponent"].Previous, 1e-12);
        Assert.AreEqual(0, rows["GoneComponent"].Current);
        StringAssert.StartsWith(reports.ComparisonMessageFor(LiveView.Memory), "Memory in MB");

        harness.Click(reports.TabTileFor(LiveView.FrameCosts));

        Assert.AreEqual(LiveView.FrameCosts, reports.SelectedTab);
        Assert.IsEmpty(reports.ComparisonListFor(LiveView.FrameCosts).RowsInDisplayOrder);
        Assert.AreEqual("Neither file holds frame costs.", reports.ComparisonMessageFor(LiveView.FrameCosts));
    }

    [TestMethod]
    public void TwoStartupReports_CompareEachPhaseByItsPath()
    {
        using var harness = OpenReports();
        var reports = harness.View.Reports;
        reports.UseAsBaseline((DiagnosticsReportEntry)RowOf(reports, OlderStartupFileName).Tag!);

        reports.CompareWithBaseline(EntryOf(reports, DiagnosticsReportKind.Startup));

        var rows = reports.ComparisonListFor(LiveView.Startup).RowsInDisplayOrder.Select(static row => (BenchmarkComparisonRow)row.Tag!).ToDictionary(static row => row.Name);
        Assert.AreEqual(600, rows["World Session Setup"].Previous);
        Assert.AreEqual(900, rows["World Session Setup"].Current);
        Assert.AreEqual(40, rows["World Session Setup / Configure"].Previous);
        Assert.AreEqual(0, rows["World Session Setup / Configure"].Current);
    }

    [TestMethod]
    public void TheComparisonTabs_AreFrameCostsMemoryLeaksAndStartup()
    {
        CollectionAssert.AreEqual(new[] { LiveView.FrameCosts, LiveView.Memory, LiveView.Leaks, LiveView.Startup }, ReportComparison.Views.ToArray());
    }
}
