using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class FrameDeadlineTests
{
    private struct Tick : IScheduledTimer
    {
        public uint NextTickFrame { get; set; }
        public uint TimerWheelMark { get; set; }
    }

    /// <summary>
    /// The property RepeatEvery exists to guarantee: the period counts from the deadline that fired,
    /// not the frame it was handled on. A firing that ran 4 frames late still leaves the schedule on
    /// its original cadence -- After(now, period) would have slid the whole series by those 4 frames.
    /// </summary>
    [TestMethod]
    public void RepeatEvery_CountsFromTheFiredDeadline_NotTheFrameItWasHandledOn()
    {
        var timer = new Tick { NextTickFrame = 100 };

        timer.RepeatEvery(30);

        Assert.AreEqual(130u, timer.NextTickFrame, "Handled on frame 104, but the cadence is 100, 130, 160 -- not 104, 134.");

        timer.RepeatEvery(30);

        Assert.AreEqual(160u, timer.NextTickFrame);
    }

    /// <summary>Owed firings are caught up one per frame rather than skipped: a deadline far enough behind stays behind until the wheel walks it forward.</summary>
    [TestMethod]
    public void RepeatEvery_AfterALongGap_DoesNotSkipTheFiringsTheGapCovered()
    {
        var timer = new Tick { NextTickFrame = 100 };

        timer.RepeatEvery(30);

        Assert.IsTrue(FrameDeadline.IsReached(timer.NextTickFrame, now: 500), "Still owed, and still due -- the gap didn't erase it.");
    }

    [TestMethod]
    public void RepeatEvery_ParkedTimer_StaysParked()
    {
        var timer = new Tick { NextTickFrame = FrameDeadline.Never };

        timer.RepeatEvery(30);

        Assert.AreEqual(FrameDeadline.Never, timer.NextTickFrame);
    }

    /// <summary>D frames of wait means running on frames F .. F+D-1 and reached from F+D -- the same count the old "D frames remaining" meant.</summary>
    [TestMethod]
    public void After_RunsForExactlyThatManyFrames()
    {
        var deadline = FrameDeadline.After(now: 100, frames: 30);

        Assert.IsFalse(FrameDeadline.IsReached(deadline, 100));
        Assert.IsFalse(FrameDeadline.IsReached(deadline, 129));
        Assert.IsTrue(FrameDeadline.IsReached(deadline, 130));
        Assert.AreEqual(30, FrameDeadline.Remaining(deadline, 100));
        Assert.AreEqual(1, FrameDeadline.Remaining(deadline, 129));
        Assert.AreEqual(0, FrameDeadline.Remaining(deadline, 130));
        Assert.AreEqual(0, FrameDeadline.Remaining(deadline, 10_000), "Clamped at 0 long after, never negative.");
    }

    [TestMethod]
    public void After_ZeroOrNegativeFrames_IsReachedImmediately()
    {
        Assert.IsTrue(FrameDeadline.IsReached(FrameDeadline.After(50, 0), 50));
        Assert.IsTrue(FrameDeadline.IsReached(FrameDeadline.After(50, -5), 50));
    }

    /// <summary>A default (0) deadline is what a freshly created component carries -- it must read as "not waiting".</summary>
    [TestMethod]
    public void DefaultDeadline_IsAlwaysReached()
    {
        Assert.IsTrue(FrameDeadline.IsReached(default, 0));
        Assert.AreEqual(0, FrameDeadline.Remaining(default, 0));
    }

    /// <summary>Never is reserved for parked/permanent timers, so After must never produce it, even at the end of the frame range.</summary>
    [TestMethod]
    public void After_SaturatesBelowNever()
    {
        var deadline = FrameDeadline.After(now: uint.MaxValue - 10, frames: 1_000);

        Assert.AreEqual(FrameDeadline.Never - 1, deadline);
        Assert.IsFalse(FrameDeadline.IsReached(FrameDeadline.Never, uint.MaxValue - 1));
        Assert.AreEqual(int.MaxValue, FrameDeadline.Remaining(FrameDeadline.Never, 0));
    }

    [TestMethod]
    public void SystemManager_AdvancesItsClockBeforeAnySystemRuns()
    {
        var manager = new SystemManager();
        long frameSeenBySystem = -1;
        manager.Register(new ClockReadingSystem(manager.Clock, frame => frameSeenBySystem = frame));

        manager.Update(new EngineTime(default, default, false, 42));

        Assert.AreEqual(42, frameSeenBySystem, "A system (or anything it calls) reading the clock mid-update sees the frame being simulated.");
        Assert.AreEqual(42, manager.Clock.CurrentFrame, "And it still reads that frame after the update, for Presentation.");
    }

    /// <summary>The game swaps in the clock its modules captured; SystemManager must advance that instance, not a private one.</summary>
    [TestMethod]
    public void SystemManager_AdvancesAnInjectedClock()
    {
        var clock = new SimulationClock();
        var manager = new SystemManager { Clock = clock };

        manager.Update(new EngineTime(default, default, false, 7));

        Assert.AreEqual(7, clock.CurrentFrame);
    }

    private sealed class ClockReadingSystem(SimulationClock clock, Action<long> onUpdate) : ISystem
    {
        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex) => onUpdate(clock.CurrentFrame);
    }
}
