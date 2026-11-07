using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
public sealed class GlowPulseClockTests
{
    [TestMethod]
    public void NeverStarted_ReadsAsJustStarting()
    {
        var clock = new GlowPulseClock();

        Assert.AreEqual(TimeSpan.Zero, clock.ElapsedAt(TimeSpan.FromSeconds(7)));
    }

    [TestMethod]
    public void NeverStarted_StartsAtItsFirstStartPending()
    {
        var clock = new GlowPulseClock();

        clock.StartPendingAt(TimeSpan.FromSeconds(7));

        Assert.AreEqual(TimeSpan.FromSeconds(2), clock.ElapsedAt(TimeSpan.FromSeconds(9)));
    }

    [TestMethod]
    public void ElapsedAt_NeverStartsThePulse()
    {
        var clock = new GlowPulseClock();
        clock.ElapsedAt(TimeSpan.FromSeconds(1));

        clock.StartPendingAt(TimeSpan.FromSeconds(5));

        Assert.AreEqual(TimeSpan.FromSeconds(1), clock.ElapsedAt(TimeSpan.FromSeconds(6)));
    }

    [TestMethod]
    public void StartPending_WithNothingPending_LeavesThePulseRunning()
    {
        var clock = new GlowPulseClock();
        clock.StartPendingAt(TimeSpan.FromSeconds(1));

        clock.StartPendingAt(TimeSpan.FromSeconds(5));

        Assert.AreEqual(TimeSpan.FromSeconds(5), clock.ElapsedAt(TimeSpan.FromSeconds(6)));
    }

    [TestMethod]
    public void Restart_ReadsAsJustStartingUntilTheNextStartPending()
    {
        var clock = new GlowPulseClock();
        clock.StartPendingAt(TimeSpan.FromSeconds(1));

        clock.Restart();

        Assert.AreEqual(TimeSpan.Zero, clock.ElapsedAt(TimeSpan.FromSeconds(4)));
        clock.StartPendingAt(TimeSpan.FromSeconds(5));
        Assert.AreEqual(TimeSpan.FromSeconds(1), clock.ElapsedAt(TimeSpan.FromSeconds(6)));
    }

    [TestMethod]
    public void TwoClocksRestartedBeforeTheSameStart_StayInStep()
    {
        var first = new GlowPulseClock();
        var second = new GlowPulseClock();
        first.StartPendingAt(TimeSpan.FromSeconds(1));
        second.StartPendingAt(TimeSpan.FromSeconds(3));

        first.Restart();
        second.Restart();
        first.StartPendingAt(TimeSpan.FromSeconds(10));
        second.StartPendingAt(TimeSpan.FromSeconds(10));

        Assert.AreEqual(first.ElapsedAt(TimeSpan.FromSeconds(12.5)), second.ElapsedAt(TimeSpan.FromSeconds(12.5)));
    }

    [TestMethod]
    public void Reset_ReturnsToNeverStarted()
    {
        var clock = new GlowPulseClock();
        clock.StartPendingAt(TimeSpan.FromSeconds(1));

        clock.Reset();

        Assert.AreEqual(TimeSpan.Zero, clock.ElapsedAt(TimeSpan.FromSeconds(4)));
        clock.StartPendingAt(TimeSpan.FromSeconds(4));
        Assert.AreEqual(TimeSpan.FromSeconds(1), clock.ElapsedAt(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void ATimeBeforeTheStart_ReadsAsZero()
    {
        var clock = new GlowPulseClock();
        clock.StartPendingAt(TimeSpan.FromSeconds(4));

        Assert.AreEqual(TimeSpan.Zero, clock.ElapsedAt(TimeSpan.FromSeconds(2)));
    }
}
