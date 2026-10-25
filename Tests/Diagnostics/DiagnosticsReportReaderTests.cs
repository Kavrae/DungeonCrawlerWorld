using System.Text;
using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticsReportReaderTests
{
    private static readonly FrameCostEntry[] FrameCosts =
    [
        new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 22.5),
        new(FrameCostCategory.Update, "SystemManager", "MovementSystem", 11.25),
        new(FrameCostCategory.Update, "EventBus", "EntityDamagedEvent", 0.5),
        new(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", 80),
    ];

    private static readonly ComponentMemoryEntry[] Memory =
    [
        new("TransformComponent", 660_000, 21_120_000, ComponentMemoryTracker.EveryEntityPopulation),
        new("HealthComponent", 1_200, 96_000, "Built"),
        new("OrphanComponent", 0, 4_096),
    ];

    private static readonly LeakFinding[] Leaks = [new("HealthComponent", "Grew 40% beyond its population.", 0.4)];

    private static readonly GaugeIntervalSummary[] Gauges =
    [
        new("Process", "HeapBytes", GaugeKind.Level, 1_000, 900, 1_100, 0, 0),
        new("Process", "Gen0Collections", GaugeKind.Cumulative, 1, 0, 2, 7, 5),
    ];

    [TestMethod]
    public void EveryReportTheWriterWrites_ReadsBackAsWritten()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"diagnostics-report-{Guid.NewGuid():N}");
        try
        {
            var json = DiagnosticsReportWriter.Write(outputDirectory, DiagnosticsFeatures.All, 42, FrameCosts, Memory, Leaks, Gauges);

            var report = DiagnosticsReportReader.ReadPeriodicReport(File.ReadAllBytes(Path.Combine(outputDirectory, "latest.json")));

            Assert.AreEqual(json, File.ReadAllText(Path.Combine(outputDirectory, "latest.json")));
            Assert.AreEqual(DiagnosticsFeatures.All.ToString(), report.Features);
            Assert.AreEqual(42, report.RandomSeed);
            CollectionAssert.AreEquivalent(FrameCosts, DiagnosticsReportReader.FrameCostsOf(report).ToArray());
            CollectionAssert.AreEqual(
                Memory.Select(static entry => new DiagnosticsReportMemoryItem(entry.ComponentTypeName, entry.Count, entry.EstimatedBytes, entry.Population)).ToArray(),
                report.Memory!.ToArray());
            Assert.AreEqual(new DiagnosticsReportLeak("HealthComponent", "Grew 40% beyond its population.", 0.4), report.Leaks!.Single());
            Assert.AreEqual(new DiagnosticsReportGauge("Process", "HeapBytes", "Level", 1_000, 900, 1_100, null, null, null), report.Gauges![0]);
            Assert.AreEqual(new DiagnosticsReportGauge("Process", "Gen0Collections", "Cumulative", null, null, null, 7, 2, 5), report.Gauges[1]);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void AReportWithFeaturesOff_ReadsWithThoseSectionsMissing()
    {
        var report = DiagnosticsReportReader.ReadPeriodicReport(Encoding.UTF8.GetBytes(DiagnosticsReportWriter.Serialize(
            DiagnosticsReportWriter.Build(DateTime.UtcNow, DiagnosticsFeatures.Gauges, null, null, null, null, Gauges))));

        Assert.IsNull(report.FrameBudget);
        Assert.IsNull(report.Memory);
        Assert.IsNull(report.Leaks);
        Assert.IsEmpty(DiagnosticsReportReader.FrameCostsOf(report));
        Assert.HasCount(2, report.Gauges!);
    }

    [TestMethod]
    public void TheStartupReportTheProfilerWrites_ReadsBackItsPhasesAndDepths()
    {
        var profiler = new StartupProfiler();
        profiler.ScopeStarted(new DiagnosticScope("World Session Setup", null));
        profiler.ScopeStarted(new DiagnosticScope("Configure", "Health"));
        profiler.ScopeEnded(new DiagnosticScope("Configure", "Health"), TimeSpan.FromMilliseconds(3));
        profiler.ScopeEnded(new DiagnosticScope("World Session Setup", null), TimeSpan.FromMilliseconds(40));
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"startup-report-{Guid.NewGuid():N}");
        try
        {
            var json = profiler.WriteReport(outputDirectory);

            var report = DiagnosticsReportReader.ReadStartupReport(Encoding.UTF8.GetBytes(json));

            Assert.IsFalse(report.IsStable);
            Assert.IsNull(report.TimeToStableMilliseconds);
            CollectionAssert.AreEqual(profiler.Phases.ToArray(), report.Phases.ToArray());
            Assert.AreEqual(1, report.Phases[1].Depth);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void TheBenchmarkReportTheBenchmarkWrites_ReadsBack_KeyedAsTheSkillKeysItsRuns()
    {
        var benchmark = new FrameRangeBenchmark(new BenchmarkFrameRange(1, 3));
        for (var frame = 1; frame < 3; frame++)
        {
            benchmark.SimulationFrameStarting(frame);
            benchmark.Record(FrameCostCategory.Update, "SystemManager", "AuraSystem", TimeSpan.FromMilliseconds(2));
            benchmark.Record(FrameCostCategory.Update, "Map", "Renderer.Update (probe)", TimeSpan.FromMilliseconds(1));
            benchmark.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", TimeSpan.FromMilliseconds(4));
            benchmark.SimulationFrameEnded(frame);
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"benchmark-report-{Guid.NewGuid():N}");
        try
        {
            var report = DiagnosticsReportReader.ReadBenchmarkReport(File.ReadAllBytes(benchmark.WriteReport(outputDirectory, randomSeed: 7)));

            Assert.AreEqual(7, report.RandomSeed);
            Assert.AreEqual(1, report.StartFrame);
            Assert.AreEqual(3, report.EndFrame);
            var perFrame = DiagnosticsReportReader.MillisecondsPerFrameOf(report);
            Assert.AreEqual(2, perFrame["Update.SystemManager.AuraSystem"], 1e-9);
            Assert.AreEqual(1, perFrame["Update.Map.Renderer.Update (probe)"], 1e-9);
            Assert.AreEqual(4, perFrame["Draw.GameLoop.Shell.Draw"], 1e-9);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void TheGaugeSeriesTheReportWrites_ReadsBack_FrameByFrame()
    {
        var reading = 0.0;
        var tracker = new GaugeTracker([new Gauge("Streamer", "QueuedBytes", GaugeKind.Level, () => reading)]);
        var series = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(10, 13));
        for (var frame = 10; frame < 13; frame++)
        {
            reading = frame * 100;
            tracker.Sample();
            series.Record(frame, TimeSpan.FromMilliseconds(frame));
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"gauge-report-{Guid.NewGuid():N}");
        try
        {
            var report = DiagnosticsReportReader.ReadGaugeSeriesReport(File.ReadAllBytes(series.WriteReport(outputDirectory, randomSeed: 3)));

            Assert.AreEqual(3, report.RecordedFrameCount);
            var frameCost = report.Gauges.Single(static gauge => gauge.Group == "Frame");
            CollectionAssert.AreEqual(new[] { 10.0, 11.0, 12.0 }, frameCost.Series);
            CollectionAssert.AreEqual(new[] { 1000.0, 1100.0, 1200.0 }, report.Gauges.Single(static gauge => gauge.Name == "QueuedBytes").Series);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void AParentOfAnUnknownCategory_IsLeftOut_AndTheRestRead()
    {
        DiagnosticsReportFrameCostParent[] parents = [new("Render", "GameLoop", "Draw"), new("Update", "GameLoop", "EcsContext.Update (all systems)")];
        var report = new DiagnosticsReport(
            DateTime.UtcNow,
            nameof(DiagnosticsFeatures.FrameBudget),
            null,
            new DiagnosticsReportFrameBudget(
                new Dictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>> { ["SystemManager"] = [new("MovementSystem", 2, parents)] },
                new Dictionary<string, IReadOnlyList<DiagnosticsReportFrameCost>>()),
            null,
            null,
            null);

        var entry = DiagnosticsReportReader.FrameCostsOf(report).Single();

        CollectionAssert.AreEqual(new[] { new FrameCostRowName(FrameCostCategory.Update, "GameLoop", "EcsContext.Update (all systems)") }, entry.Parents!.ToArray());
    }

    [TestMethod]
    public void SomethingElse_IsNotAReport()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => DiagnosticsReportReader.ReadPeriodicReport(Encoding.UTF8.GetBytes("{ not json")));
    }
}
