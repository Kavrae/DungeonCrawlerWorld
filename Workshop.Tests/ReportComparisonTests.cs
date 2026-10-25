using Engine.Diagnostics;
using Workshop.Debug;

namespace Workshop.Tests;

[TestClass]
public sealed class ReportComparisonTests
{
    private static LiveDiagnosticsSession SessionWith(DiagnosticsReport? report = null, DiagnosticsStartupReport? startup = null, bool frameCostsArePerFrame = false) =>
        LiveDiagnosticsSession.Recorded("file.json", new DiagnosticsLinkHello(1, 0, 1, "All"), [], null, report, startup, frameCostsArePerFrame);

    private static DiagnosticsReport ReportWith(IReadOnlyList<DiagnosticsReportLeak>? leaks = null, double auraMillisecondsPerSecond = 0) =>
        DiagnosticsReportWriter.Build(DateTime.UtcNow, DiagnosticsFeatures.All, 1,
            auraMillisecondsPerSecond > 0 ? [new FrameCostEntry(FrameCostCategory.Update, "SystemManager", "AuraSystem", auraMillisecondsPerSecond)] : null,
            null,
            leaks?.Select(static leak => new LeakFinding(leak.Subject, leak.Detail, leak.GrowthRatio)).ToList(),
            null);

    [TestMethod]
    public void LeakFindings_CompareByTheirGrowth_ANewOneReadingZeroBefore()
    {
        var previous = SessionWith(ReportWith([new DiagnosticsReportLeak("HealthComponent", "grew", 0.4)]));
        var current = SessionWith(ReportWith([new DiagnosticsReportLeak("HealthComponent", "grew", 0.6), new DiagnosticsReportLeak("DeadComponent", "grew", 0.8)]));

        var tab = ReportComparison.Compare("before.json", previous, "after.json", current, 20).TabFor(LiveView.Leaks);

        Assert.IsNull(tab.Unavailable);
        var rows = tab.Rows.ToDictionary(static row => row.Name);
        Assert.AreEqual(40, rows["HealthComponent"].Previous, 1e-9);
        Assert.AreEqual(60, rows["HealthComponent"].Current, 1e-9);
        Assert.AreEqual(0, rows["DeadComponent"].Previous);
        Assert.AreEqual(BenchmarkComparisonFlag.Regression, rows["HealthComponent"].Flag);
    }

    [TestMethod]
    public void AKindOneReportLacks_SaysWhichFile()
    {
        var comparison = ReportComparison.Compare("before.json", SessionWith(startup: new DiagnosticsStartupReport(DateTime.UtcNow, true, 1, [])), "after.json", SessionWith(), 20);

        Assert.AreEqual("after.json holds no startup report.", comparison.TabFor(LiveView.Startup).Unavailable);
        Assert.AreEqual("Neither file holds memory.", comparison.TabFor(LiveView.Memory).Unavailable);
    }

    [TestMethod]
    public void FrameCostsPerFrameAndPerSecond_AreNotCompared()
    {
        var comparison = ReportComparison.Compare("run.json", SessionWith(ReportWith(auraMillisecondsPerSecond: 0.4), frameCostsArePerFrame: true), "latest.json", SessionWith(ReportWith(auraMillisecondsPerSecond: 24)), 20);

        var tab = comparison.TabFor(LiveView.FrameCosts);
        Assert.IsEmpty(tab.Rows);
        StringAssert.StartsWith(tab.Unavailable, "Not comparable");
    }

    [TestMethod]
    public void StartupPhases_AreKeyedByTheirPath_SoTheSameNameInTwoPlacesStaysApart()
    {
        var phases = ReportComparison.StartupPhasesOf(new DiagnosticsStartupReport(DateTime.UtcNow, true, 1,
        [
            new PhaseRecord("Build", 10, 0),
            new PhaseRecord("Configure", 2, 1),
            new PhaseRecord("Trial", 5, 0),
            new PhaseRecord("Configure", 3, 1),
        ]));

        Assert.AreEqual(2, phases["Build / Configure"]);
        Assert.AreEqual(3, phases["Trial / Configure"]);
        Assert.HasCount(4, phases);
    }
}
