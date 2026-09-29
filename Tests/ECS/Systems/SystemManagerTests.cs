using Engine.Diagnostics;
using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

/// <summary>
/// Under entity striping (decision #11), SystemManager no longer gates systems on a
/// period/offset -- every registered system's Update runs every SystemManager.Update call.
/// SystemManager now owns the rotating stripe cursor centrally (previously duplicated
/// increment-and-wrap logic in every system) and passes it into Update, so these tests also
/// cover that the passed stripeIndex actually rotates 0..StripeCount-1 and wraps correctly.
/// </summary>
[TestClass]
public sealed class SystemManagerTests
{
    private sealed class RecordingSystem(byte stripeCount) : ISystem
    {
        public byte StripeCount => stripeCount;
        public int UpdateCount { get; private set; }
        public List<byte> StripeIndexesSeen { get; } = [];

        public void Update(EngineTime time, byte stripeIndex)
        {
            UpdateCount++;
            StripeIndexesSeen.Add(stripeIndex);
        }
    }

    [TestMethod]
    public void Register_ZeroStripeCount_Throws()
    {
        var systemManager = new SystemManager();

        Assert.ThrowsExactly<ArgumentException>(() => systemManager.Register(new RecordingSystem(0)));
    }

    [TestMethod]
    public void Update_CallsEveryRegisteredSystemEveryFrame()
    {
        var systemManager = new SystemManager();
        var system = new RecordingSystem(5);
        systemManager.Register(system);

        for (var frame = 0; frame < 20; frame++)
        {
            systemManager.Update(default);
        }

        Assert.AreEqual(20, system.UpdateCount);
    }

    [TestMethod]
    public void Update_MultipleSystemsWithSameStripeCount_AllFireEveryFrame()
    {
        var systemManager = new SystemManager();
        var first = new RecordingSystem(10);
        var second = new RecordingSystem(10);
        systemManager.Register(first);
        systemManager.Register(second);

        for (var frame = 0; frame < 10; frame++)
        {
            systemManager.Update(default);
        }

        // No offset/collision concept remains -- both fire on every one of the 10 frames.
        Assert.AreEqual(10, first.UpdateCount);
        Assert.AreEqual(10, second.UpdateCount);
    }

    [TestMethod]
    public void Update_NoSystemsRegistered_DoesNotThrow()
    {
        var systemManager = new SystemManager();

        systemManager.Update(default);
    }

    [TestMethod]
    public void Update_StripeIndexRotatesZeroToStripeCountMinusOneThenWraps()
    {
        var systemManager = new SystemManager();
        var system = new RecordingSystem(3);
        systemManager.Register(system);

        for (var frame = 0; frame < 7; frame++)
        {
            systemManager.Update(default);
        }

        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 0, 1, 2, 0 }, system.StripeIndexesSeen);
    }

    [TestMethod]
    public void Update_TwoSystemsWithDifferentStripeCounts_EachRotatesIndependently()
    {
        var systemManager = new SystemManager();
        var fast = new RecordingSystem(2);
        var slow = new RecordingSystem(4);
        systemManager.Register(fast);
        systemManager.Register(slow);

        for (var frame = 0; frame < 4; frame++)
        {
            systemManager.Update(default);
        }

        CollectionAssert.AreEqual(new byte[] { 0, 1, 0, 1 }, fast.StripeIndexesSeen);
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3 }, slow.StripeIndexesSeen);
    }

    private sealed class OrderRecordingSystem(string name, List<string> order) : ISystem
    {
        public byte StripeCount => 1;

        public void Update(EngineTime time, byte stripeIndex) => order.Add(name);
    }

    [TestMethod]
    public void RegisterFirst_RunsBeforeSystemsRegisteredEarlier()
    {
        var order = new List<string>();
        var systemManager = new SystemManager();
        systemManager.Register(new OrderRecordingSystem("a", order));
        systemManager.Register(new OrderRecordingSystem("b", order));
        systemManager.RegisterFirst(new OrderRecordingSystem("first", order));

        systemManager.Update(default);

        CollectionAssert.AreEqual(new[] { "first", "a", "b" }, order);
    }

    private sealed class SimulationFrameRecorder(List<string> order, SimulationClock clock) : ISimulationFrameListener
    {
        public void SimulationFrameStarting(long frameCount) => order.Add($"starting {frameCount} at clock {clock.CurrentFrame}");

        public void SimulationFrameEnded(long frameCount, TimeSpan elapsed) => order.Add($"ended {frameCount} at clock {clock.CurrentFrame}");
    }

    [TestMethod]
    [DoNotParallelize]
    public void Update_EmitsFrameStartBeforeTheClockAdvances_AndFrameEndAfterEverySystem()
    {
        var order = new List<string>();
        var systemManager = new SystemManager();
        systemManager.Register(new OrderRecordingSystem("system", order));
        using var simulationFrameSubscription = EngineHooks.SimulationFrames.Subscribe(new SimulationFrameRecorder(order, systemManager.Clock));

        systemManager.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: 7));

        CollectionAssert.AreEqual(new[] { "starting 7 at clock 0", "system", "ended 7 at clock 7" }, order);
    }
}