using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class GaugeHistoryTests
{
    private static (GaugeHistory History, GaugeTracker Tracker, Action<double> SetLevel) Create()
    {
        var level = 0d;
        var tracker = new GaugeTracker([new Gauge("Test", "Level", GaugeKind.Level, () => level)]);
        return (new GaugeHistory(tracker), tracker, value => level = value);
    }

    private static void RecordFrames(GaugeHistory history, GaugeTracker tracker, Action<double> setLevel, int firstFrame, int lastFrame)
    {
        for (var frame = firstFrame; frame <= lastFrame; frame++)
        {
            setLevel(frame);
            tracker.Sample();
            history.Record(frame, TimeSpan.FromMilliseconds(frame * 2));
        }
    }

    [TestMethod]
    public void Rows_StartWithTheFrameUpdateCost_ThenEveryGauge()
    {
        var (history, _, _) = Create();

        Assert.AreEqual(("Frame", "UpdateMilliseconds"), (history.Rows[0].GroupName, history.Rows[0].GaugeName));
        Assert.AreEqual(("Test", "Level"), (history.Rows[1].GroupName, history.Rows[1].GaugeName));
    }

    [TestMethod]
    public void CopyHistory_BeforeTheRingFills_CopiesEveryFrameOldestFirst()
    {
        var (history, tracker, setLevel) = Create();
        RecordFrames(history, tracker, setLevel, 1, 3);
        var destination = new double[GaugeHistory.HistoryFrameCount];

        var copiedCount = history.CopyHistory(1, destination);

        CollectionAssert.AreEqual(new[] { 1d, 2d, 3d }, destination[..copiedCount]);
        Assert.AreEqual((3L, 3d, 6d), (history.LatestFrame, history.GetLatestValue(1), history.GetLatestValue(0)));
    }

    [TestMethod]
    public void CopyHistory_AfterTheRingWraps_KeepsTheLatestFramesOldestFirst()
    {
        var (history, tracker, setLevel) = Create();
        RecordFrames(history, tracker, setLevel, 1, GaugeHistory.HistoryFrameCount + 5);
        var destination = new double[GaugeHistory.HistoryFrameCount];

        var copiedCount = history.CopyHistory(1, destination);

        Assert.AreEqual(GaugeHistory.HistoryFrameCount, copiedCount);
        CollectionAssert.AreEqual(Enumerable.Range(6, GaugeHistory.HistoryFrameCount).Select(static frame => (double)frame).ToArray(), destination);
        Assert.AreEqual(GaugeHistory.HistoryFrameCount + 5d, history.GetLatestValue(1));
    }

    [TestMethod]
    public void GetLatestValue_BeforeAnyFrame_IsZero()
    {
        var (history, _, _) = Create();

        Assert.AreEqual(0d, history.GetLatestValue(1));
    }

    [TestMethod]
    public void Record_AllocatesNothing()
    {
        var (history, tracker, setLevel) = Create();
        RecordFrames(history, tracker, setLevel, 1, 1);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        RecordFrames(history, tracker, setLevel, 2, 700);

        Assert.AreEqual(allocatedBefore, GC.GetAllocatedBytesForCurrentThread());
    }
}
