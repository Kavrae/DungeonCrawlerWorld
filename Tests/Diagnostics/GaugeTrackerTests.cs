using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class GaugeTrackerTests
{
    [TestMethod]
    public void Sample_LevelGauge_RecordsTheValueAsRead()
    {
        var level = 7d;
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => level)]);

        tracker.Sample();
        level = 3;
        tracker.Sample();

        Assert.AreEqual(3d, tracker.GetLatestValue(0));
    }

    [TestMethod]
    public void Sample_CumulativeGauge_FirstValueIsTheChangeSinceConstruction()
    {
        var counter = 100d;
        var tracker = new GaugeTracker([new Gauge("Test", "Counter", GaugeKind.Cumulative, () => counter)]);

        counter = 104;
        tracker.Sample();

        Assert.AreEqual(4d, tracker.GetLatestValue(0));
    }

    [TestMethod]
    public void Sample_CumulativeGauge_RecordsEachFramesChange()
    {
        var counter = 0d;
        var tracker = new GaugeTracker([new Gauge("Test", "Counter", GaugeKind.Cumulative, () => counter)]);

        counter = 5;
        tracker.Sample();
        counter = 7;
        tracker.Sample();

        Assert.AreEqual(2d, tracker.GetLatestValue(0));
    }

    [TestMethod]
    public void TakeIntervalSummaries_LevelGauge_HasLastMinimumAndMaximum()
    {
        var level = 0d;
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => level)]);

        foreach (var value in new[] { 5d, 2d, 9d, 4d })
        {
            level = value;
            tracker.Sample();
        }

        var summary = tracker.TakeIntervalSummaries().Single();
        Assert.AreEqual((4d, 2d, 9d), (summary.Last, summary.Minimum, summary.Maximum));
    }

    [TestMethod]
    public void TakeIntervalSummaries_CumulativeGauge_HasTotalMaximumPerFrameAndFramesNonzero()
    {
        var counter = 0d;
        var tracker = new GaugeTracker([new Gauge("Test", "Counter", GaugeKind.Cumulative, () => counter)]);

        foreach (var value in new[] { 0d, 3d, 3d, 4d })
        {
            counter = value;
            tracker.Sample();
        }

        var summary = tracker.TakeIntervalSummaries().Single();
        Assert.AreEqual((4d, 3d, 2), (summary.Total, summary.Maximum, summary.FramesNonzero));
    }

    [TestMethod]
    public void TakeIntervalSummaries_StartsANewInterval()
    {
        var level = 10d;
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => level)]);
        tracker.Sample();
        tracker.TakeIntervalSummaries();

        level = 1;
        tracker.Sample();

        var summary = tracker.TakeIntervalSummaries().Single();
        Assert.AreEqual((1d, 1d), (summary.Minimum, summary.Maximum));
    }

    [TestMethod]
    public void TakeIntervalSummaries_NothingSampledSinceTheLast_IsEmpty()
    {
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => 1)]);
        tracker.Sample();
        tracker.TakeIntervalSummaries();

        Assert.IsEmpty(tracker.TakeIntervalSummaries());
    }

    [TestMethod]
    public void Sample_AllocatesNothing()
    {
        var counter = 0d;
        var tracker = new GaugeTracker(
        [
            new Gauge("Test", "Level", GaugeKind.Level, () => counter),
            new Gauge("Test", "Counter", GaugeKind.Cumulative, () => counter),
            .. GaugeTracker.ProcessGauges,
        ]);
        tracker.Sample();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 100; frame++)
        {
            counter = frame;
            tracker.Sample();
        }

        Assert.AreEqual(allocatedBefore, GC.GetAllocatedBytesForCurrentThread());
    }
}
