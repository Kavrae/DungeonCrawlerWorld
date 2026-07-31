using Engine.Diagnostics;
using Engine.Events;

namespace Tests.Events;

[TestClass]
public sealed class EventBusTests
{
    private sealed record TestEvent(int Value);
    private sealed record OtherEvent;
    private sealed record BufferedTestEvent(int Value) : IBufferedEvent;

    [TestMethod]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = new EventBus();

        bus.Publish(new TestEvent(1));
    }

    [TestMethod]
    public void Publish_InvokesSubscriber()
    {
        var bus = new EventBus();
        var received = -1;
        bus.Subscribe<TestEvent>(e => received = e.Value);

        bus.Publish(new TestEvent(42));

        Assert.AreEqual(42, received);
    }

    [TestMethod]
    public void Publish_MultipleSubscribers_InvokesAll()
    {
        var bus = new EventBus();
        var count = 0;
        bus.Subscribe<TestEvent>(_ => count++);
        bus.Subscribe<TestEvent>(_ => count++);

        bus.Publish(new TestEvent(1));

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void Publish_OnlyInvokesSubscribersOfMatchingType()
    {
        var bus = new EventBus();
        var testEventFired = false;
        var otherEventFired = false;
        bus.Subscribe<TestEvent>(_ => testEventFired = true);
        bus.Subscribe<OtherEvent>(_ => otherEventFired = true);

        bus.Publish(new TestEvent(1));

        Assert.IsTrue(testEventFired);
        Assert.IsFalse(otherEventFired);
    }

    [TestMethod]
    public void Unsubscribe_StopsReceivingFutureEvents()
    {
        var bus = new EventBus();
        var count = 0;
        void Handler(TestEvent e) => count++;
        bus.Subscribe<TestEvent>(Handler);

        bus.Unsubscribe<TestEvent>(Handler);
        bus.Publish(new TestEvent(1));

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void Publish_NonBufferedEvent_StillDispatchesImmediately()
    {
        var bus = new EventBus();
        var received = -1;
        bus.Subscribe<TestEvent>(e => received = e.Value);

        bus.Publish(new TestEvent(7));

        Assert.AreEqual(7, received);
    }

    [TestMethod]
    public void Publish_BufferedEvent_DoesNotDispatchUntilDispatchBuffered()
    {
        var bus = new EventBus();
        var received = -1;
        bus.Subscribe<BufferedTestEvent>(e => received = e.Value);

        bus.Publish(new BufferedTestEvent(9));

        Assert.AreEqual(-1, received);

        bus.DispatchBuffered<BufferedTestEvent>();

        Assert.AreEqual(9, received);
    }

    [TestMethod]
    public void DispatchBuffered_MultiplePublishesBeforeOneDispatch_DeliversAllInOrder()
    {
        var bus = new EventBus();
        var received = new List<int>();
        bus.Subscribe<BufferedTestEvent>(e => received.Add(e.Value));

        bus.Publish(new BufferedTestEvent(1));
        bus.Publish(new BufferedTestEvent(2));
        bus.Publish(new BufferedTestEvent(3));

        bus.DispatchBuffered<BufferedTestEvent>();

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
    }

    [TestMethod]
    public void DispatchBuffered_NothingQueued_DoesNotThrow()
    {
        var bus = new EventBus();

        bus.DispatchBuffered<BufferedTestEvent>();
    }

    [TestMethod]
    public void DispatchBuffered_CalledTwice_SecondCallDeliversNothingNew()
    {
        var bus = new EventBus();
        var received = new List<int>();
        bus.Subscribe<BufferedTestEvent>(e => received.Add(e.Value));
        bus.Publish(new BufferedTestEvent(1));
        bus.DispatchBuffered<BufferedTestEvent>();

        bus.DispatchBuffered<BufferedTestEvent>();

        CollectionAssert.AreEqual(new[] { 1 }, received);
    }

    // Profiler-enabled dispatch is a separate code path from the default -- these cover its
    // functional correctness (still dispatches to the right handlers) rather than its timing
    // output, the same reason PerformanceCounter/PhaseProfiler themselves have no direct tests:
    // TopPhases only populates once a real wall-clock second has elapsed, which isn't something
    // to assert on in a fast unit test.

    [TestMethod]
    public void Publish_WithProfilerSet_StillInvokesSubscriber()
    {
        var bus = new EventBus { Profiler = new PhaseProfiler() };
        var received = -1;
        bus.Subscribe<TestEvent>(e => received = e.Value);

        bus.Publish(new TestEvent(42));

        Assert.AreEqual(42, received);
    }

    [TestMethod]
    public void Publish_WithProfilerSet_InvokesEveryHandlerExactlyOnce()
    {
        var bus = new EventBus { Profiler = new PhaseProfiler() };
        var firstCount = 0;
        var secondCount = 0;
        bus.Subscribe<TestEvent>(_ => firstCount++);
        bus.Subscribe<TestEvent>(_ => secondCount++);

        bus.Publish(new TestEvent(1));
        bus.Publish(new TestEvent(1));

        Assert.AreEqual(2, firstCount);
        Assert.AreEqual(2, secondCount);
    }
}