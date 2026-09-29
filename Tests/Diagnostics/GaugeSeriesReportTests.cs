using System.Text.Json;
using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class GaugeSeriesReportTests
{
    private sealed class SampledGauges
    {
        public double Level { get; set; }
        public double Counter { get; set; }

        public GaugeTracker CreateTracker() => new(
        [
            new Gauge("Test", "Level", GaugeKind.Level, () => Level),
            new Gauge("Test", "Counter", GaugeKind.Cumulative, () => Counter),
        ]);
    }

    private static void RunFrames(SampledGauges gauges, GaugeTracker tracker, GaugeSeriesReport report, long firstFrame, long lastFrame)
    {
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            gauges.Level = frame * 10;
            gauges.Counter += frame;
            tracker.Sample();
            report.Record(frame, TimeSpan.FromMilliseconds(frame));
        }
    }

    private static JsonElement WriteAndParse(GaugeSeriesReport report)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"gauge-series-{Guid.NewGuid():N}");
        try
        {
            var path = report.WriteReport(outputDirectory, randomSeed: 1);
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static JsonElement FindGauge(JsonElement report, string groupName, string gaugeName) =>
        report.GetProperty("Gauges").EnumerateArray().Single(gauge => gauge.GetProperty("Group").GetString() == groupName && gauge.GetProperty("Name").GetString() == gaugeName);

    private static double[] ReadSeries(JsonElement gauge) =>
        gauge.GetProperty("Series").EnumerateArray().Select(static value => value.GetDouble()).ToArray();

    [TestMethod]
    public void Record_OnlyInsideTheRange_AndCompletesOnItsLastFrame()
    {
        var gauges = new SampledGauges();
        var tracker = gauges.CreateTracker();
        var report = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(5, 8));

        RunFrames(gauges, tracker, report, 1, 6);
        Assert.AreEqual((2L, false), (report.RecordedFrameCount, report.IsComplete));

        RunFrames(gauges, tracker, report, 7, 7);
        Assert.AreEqual((3L, true), (report.RecordedFrameCount, report.IsComplete));
    }

    [TestMethod]
    public void WriteReport_EachSeriesHoldsOneValuePerFrame_StartFrameFirst()
    {
        var gauges = new SampledGauges();
        var tracker = gauges.CreateTracker();
        var report = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(5, 8));
        RunFrames(gauges, tracker, report, 1, 7);

        var written = WriteAndParse(report);

        CollectionAssert.AreEqual(new[] { 50d, 60d, 70d }, ReadSeries(FindGauge(written, "Test", "Level")));
        CollectionAssert.AreEqual(new[] { 5d, 6d, 7d }, ReadSeries(FindGauge(written, "Test", "Counter")));
        CollectionAssert.AreEqual(new[] { 5d, 6d, 7d }, ReadSeries(FindGauge(written, "Frame", "UpdateMilliseconds")));
    }

    [TestMethod]
    public void WriteReport_SummarizesEachGaugeByItsKind()
    {
        var gauges = new SampledGauges();
        var tracker = gauges.CreateTracker();
        var report = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(5, 8));
        RunFrames(gauges, tracker, report, 1, 7);

        var written = WriteAndParse(report);

        var level = FindGauge(written, "Test", "Level");
        Assert.AreEqual((50d, 70d, 60d, 7L), (level.GetProperty("Minimum").GetDouble(), level.GetProperty("Maximum").GetDouble(), level.GetProperty("Mean").GetDouble(), level.GetProperty("MaximumFrame").GetInt64()));
        Assert.IsFalse(level.TryGetProperty("Total", out _));

        var counter = FindGauge(written, "Test", "Counter");
        Assert.AreEqual((18d, 7d, 3), (counter.GetProperty("Total").GetDouble(), counter.GetProperty("MaximumPerFrame").GetDouble(), counter.GetProperty("FramesNonzero").GetInt32()));
        Assert.IsFalse(counter.TryGetProperty("Mean", out _));
    }

    [TestMethod]
    public void WriteReport_RoundsValues()
    {
        var level = 0d;
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => level)]);
        var report = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(1, 2));
        level = 7.542000000003;
        tracker.Sample();
        report.Record(1, TimeSpan.Zero);

        var written = WriteAndParse(report);

        CollectionAssert.AreEqual(new[] { 7.542 }, ReadSeries(FindGauge(written, "Test", "Level")));
    }

    [TestMethod]
    public void Record_AllocatesNothing()
    {
        var gauges = new SampledGauges();
        var tracker = gauges.CreateTracker();
        var report = new GaugeSeriesReport(tracker, new BenchmarkFrameRange(1, 1000));
        RunFrames(gauges, tracker, report, 1, 1);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        RunFrames(gauges, tracker, report, 2, 500);

        Assert.AreEqual(allocatedBefore, GC.GetAllocatedBytesForCurrentThread());
    }
}
