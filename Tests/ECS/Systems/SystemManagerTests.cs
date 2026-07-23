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
}