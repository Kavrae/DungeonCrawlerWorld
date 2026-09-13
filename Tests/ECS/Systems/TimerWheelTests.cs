using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class TimerWheelTests
{
    private static List<TimerEntry> Drain(TimerWheel wheel, long now)
    {
        var due = new List<TimerEntry>();
        wheel.DrainDue(now, due);
        return due;
    }

    [TestMethod]
    public void Schedule_FiresOnExactlyItsFrame()
    {
        var wheel = new TimerWheel(slotCount: 16);
        wheel.Schedule(entityId: 5, key: 0, deadline: 7);

        for (var frame = 0; frame < 7; frame++)
        {
            Assert.IsEmpty(Drain(wheel, frame), $"Nothing due on frame {frame}.");
        }

        CollectionAssert.AreEqual(new[] { new TimerEntry(5, 0, 7) }, Drain(wheel, 7));
        Assert.IsEmpty(Drain(wheel, 8), "Fired once, then forgotten.");
        Assert.AreEqual(0, wheel.PendingCount);
    }

    /// <summary>Late, never lost: a deadline already behind the next frame to drain fires on that drain.</summary>
    [TestMethod]
    public void Schedule_PastDeadline_FiresOnTheNextDrain()
    {
        var wheel = new TimerWheel(slotCount: 16);
        Drain(wheel, 10);

        wheel.Schedule(1, 0, deadline: 3);

        CollectionAssert.AreEqual(new[] { new TimerEntry(1, 0, 3) }, Drain(wheel, 11));
    }

    [TestMethod]
    public void Schedule_Never_IsNeverScheduled()
    {
        var wheel = new TimerWheel(slotCount: 16);

        wheel.Schedule(1, 0, FrameDeadline.Never);

        Assert.AreEqual(0, wheel.PendingCount);
    }

    [TestMethod]
    public void DrainDue_SkippingFrames_DrainsEachInFrameThenSchedulingOrder()
    {
        var wheel = new TimerWheel(slotCount: 16);
        wheel.Schedule(1, 0, 5);
        wheel.Schedule(2, 0, 3);
        wheel.Schedule(3, 0, 5);
        wheel.Schedule(4, 0, 4);

        var due = Drain(wheel, 9);

        CollectionAssert.AreEqual(new[] { 2, 4, 1, 3 }, due.Select(static e => e.EntityId).ToArray());
    }

    /// <summary>Deadlines far beyond one revolution wait in overflow and still land on their exact frame.</summary>
    [TestMethod]
    public void Schedule_BeyondOneRevolution_FiresOnItsExactFrame()
    {
        var wheel = new TimerWheel(slotCount: 16);
        wheel.Schedule(1, 0, deadline: 16);
        wheel.Schedule(2, 0, deadline: 40);
        wheel.Schedule(3, 0, deadline: 100);

        var firedOn = new Dictionary<int, long>();
        for (var frame = 0; frame <= 120; frame++)
        {
            foreach (var entry in Drain(wheel, frame))
            {
                firedOn.Add(entry.EntityId, frame);
            }
        }

        Assert.AreEqual(16, firedOn[1]);
        Assert.AreEqual(40, firedOn[2]);
        Assert.AreEqual(100, firedOn[3]);
    }

    /// <summary>
    /// The property everything else rests on, checked against a brute-force model: every entry fires
    /// exactly once, on max(deadline, first frame still to drain when it was scheduled) -- across
    /// many revolutions, overflow boundaries and entries scheduled mid-run.
    /// </summary>
    [TestMethod]
    public void RandomizedSchedule_MatchesBruteForceModel()
    {
        var (expected, actual, pendingCount) = RunRandomized(seed: 12345);

        // Within one frame, order is slot-entry order (see TimerWheel's remarks), so compare each
        // frame's contents, not their order -- the next test pins the order down separately.
        static List<(long, int)> Normalize(IEnumerable<(long Frame, TimerEntry Entry)> fires) =>
            fires.Select(static f => (f.Frame, f.Entry.EntityId)).OrderBy(static f => f.Frame).ThenBy(static f => f.EntityId).ToList();

        var expectedFired = expected.Where(static e => e.Frame <= LastFrame).ToList();
        CollectionAssert.AreEqual(Normalize(expectedFired), Normalize(actual));
        Assert.AreEqual(expected.Count - expectedFired.Count, pendingCount, "Everything not yet due is still pending.");
    }

    /// <summary>Same inputs, same order -- the determinism seeded runs rely on, including the overflow-joins-behind case.</summary>
    [TestMethod]
    public void RandomizedSchedule_FiresInTheSameOrderEveryRun()
    {
        CollectionAssert.AreEqual(RunRandomized(seed: 777).Actual, RunRandomized(seed: 777).Actual);
    }

    private const int LastFrame = 1_000;

    private static (List<(long Frame, TimerEntry Entry)> Expected, List<(long Frame, TimerEntry Entry)> Actual, int PendingCount) RunRandomized(int seed)
    {
        const int slotCount = 32;
        var random = new Random(seed);
        var wheel = new TimerWheel(slotCount);
        var expected = new List<(long, TimerEntry)>();
        var actual = new List<(long, TimerEntry)>();
        var id = 0;

        for (long frame = 0; frame <= LastFrame; frame++)
        {
            // Schedule before this frame's drain: the wheel's next frame to drain is `frame`.
            var toSchedule = random.Next(0, 4);
            for (var i = 0; i < toSchedule; i++)
            {
                var deadline = (uint)System.Math.Max(0, frame + random.Next(-5, 200));
                var entry = new TimerEntry(id++, 0, deadline);
                wheel.Schedule(entry.EntityId, entry.Key, entry.Deadline);
                expected.Add((System.Math.Max(deadline, frame), entry));
            }

            foreach (var entry in Drain(wheel, frame))
            {
                actual.Add((frame, entry));
            }
        }

        return (expected, actual, wheel.PendingCount);
    }

    [TestMethod]
    public void Constructor_RejectsNonPowerOfTwoSlotCount()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimerWheel(slotCount: 100));
    }
}
