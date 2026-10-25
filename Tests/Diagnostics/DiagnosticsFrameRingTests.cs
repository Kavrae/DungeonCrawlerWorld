using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticsFrameRingTests
{
    private static void Write(DiagnosticsFrameRing ring, long frame, double value, bool isPaused = false)
    {
        var values = ring.BeginWrite(frame, isPaused);
        values.Fill(value);
        ring.CommitWrite();
    }

    [TestMethod]
    public void TryRead_ReturnsRecordsOldestFirst_ThenNothing()
    {
        var ring = new DiagnosticsFrameRing(capacity: 4, valueCount: 2);
        Write(ring, 1, 10);
        Write(ring, 2, 20, isPaused: true);
        var values = new double[2];

        Assert.IsTrue(ring.TryRead(values, out var firstFrame, out var firstPaused, out var firstDropped));
        Assert.AreEqual(1, firstFrame);
        Assert.IsFalse(firstPaused);
        Assert.AreEqual(0, firstDropped);
        CollectionAssert.AreEqual(new[] { 10.0, 10.0 }, values);

        Assert.IsTrue(ring.TryRead(values, out var secondFrame, out var secondPaused, out _));
        Assert.AreEqual(2, secondFrame);
        Assert.IsTrue(secondPaused);
        CollectionAssert.AreEqual(new[] { 20.0, 20.0 }, values);

        Assert.IsFalse(ring.TryRead(values, out _, out _, out _));
    }

    [TestMethod]
    public void AWriterThatLapsTheReader_OverwritesTheOldest_AndTheNextReadCountsThem()
    {
        var ring = new DiagnosticsFrameRing(capacity: 4, valueCount: 1);
        for (var frame = 1; frame <= 10; frame++)
        {
            Write(ring, frame, frame);
        }

        var values = new double[1];
        Assert.IsTrue(ring.TryRead(values, out var frameRead, out _, out var droppedBefore));

        Assert.AreEqual(7, frameRead);
        Assert.AreEqual(7.0, values[0]);
        Assert.AreEqual(6, droppedBefore);
        Assert.AreEqual(6, ring.TotalDroppedCount);

        Assert.IsTrue(ring.TryRead(values, out var nextFrame, out _, out var nextDropped));
        Assert.AreEqual(8, nextFrame);
        Assert.AreEqual(0, nextDropped);
    }

    [TestMethod]
    public void AWriterAndAReaderOnTwoThreads_ReadEveryRecordWholeOrCountItDropped()
    {
        const int RecordCount = 200_000;
        var ring = new DiagnosticsFrameRing(capacity: 64, valueCount: 8);
        var writer = new Thread(() =>
        {
            for (var frame = 1; frame <= RecordCount; frame++)
            {
                Write(ring, frame, frame);
            }
        });

        writer.Start();
        var values = new double[8];
        long readCount = 0;
        long droppedCount = 0;
        long lastFrame = 0;
        while (lastFrame < RecordCount)
        {
            if (!ring.TryRead(values, out var frame, out _, out var droppedBefore))
            {
                continue;
            }

            Assert.IsTrue(Array.TrueForAll(values, value => value == frame), $"Record {frame} was read torn.");
            Assert.AreEqual(lastFrame + droppedBefore + 1, frame);
            readCount++;
            droppedCount += droppedBefore;
            lastFrame = frame;
        }

        writer.Join();
        Assert.AreEqual(RecordCount, readCount + droppedCount);
    }
}
