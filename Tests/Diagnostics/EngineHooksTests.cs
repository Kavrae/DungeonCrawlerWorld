using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
[DoNotParallelize]
public sealed class EngineHooksTests
{
    [TestMethod]
    public void Subscribe_SetsTheListener_UntilDisposed()
    {
        var recorder = new FrameBudgetTracker();

        var subscription = EngineHooks.FrameCosts.Subscribe(recorder);
        Assert.AreSame(recorder, EngineHooks.FrameCosts.Listener);

        subscription.Dispose();
        Assert.IsNull(EngineHooks.FrameCosts.Listener);
    }

    [TestMethod]
    public void Subscribe_WhileAnotherListens_ThrowsNamingIt_AndKeepsTheFirst()
    {
        var firstRecorder = new FrameBudgetTracker();
        using var subscription = EngineHooks.FrameCosts.Subscribe(firstRecorder);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => EngineHooks.FrameCosts.Subscribe(new FrameBudgetTracker()));

        Assert.Contains(nameof(FrameBudgetTracker), exception.Message);
        Assert.AreSame(firstRecorder, EngineHooks.FrameCosts.Listener);
    }

    [TestMethod]
    public void DisposingAStaleSubscription_LeavesTheCurrentListener()
    {
        var staleSubscription = EngineHooks.FrameCosts.Subscribe(new FrameBudgetTracker());
        staleSubscription.Dispose();
        var currentRecorder = new FrameBudgetTracker();
        using var currentSubscription = EngineHooks.FrameCosts.Subscribe(currentRecorder);

        staleSubscription.Dispose();

        Assert.AreSame(currentRecorder, EngineHooks.FrameCosts.Listener);
    }

    [TestMethod]
    public void DiagnosticsEngine_StartedTwice_Throws()
    {
        using var engine = new DiagnosticsEngine(DiagnosticsFeatures.FrameBudget);
        engine.Start();

        Assert.ThrowsExactly<InvalidOperationException>(engine.Start);
    }

    private sealed class FrameCostRecorder : IFrameCostRecorder
    {
        public List<(FrameCostCategory Category, string GroupName, string ItemName, TimeSpan Elapsed)> Records { get; } = [];

        public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed) =>
            Records.Add((category, groupName, itemName, elapsed));
    }

    [TestMethod]
    public void FrameCost_Disposed_RecordsOnceWithItsCategoryGroupAndItem()
    {
        var recorder = new FrameCostRecorder();
        using var subscription = EngineHooks.FrameCosts.Subscribe(recorder);

        using (EngineHooks.FrameCost(FrameCostCategory.Draw, "GameLoop", "Shell.Draw"))
        {
        }

        Assert.HasCount(1, recorder.Records);
        var (category, groupName, itemName, elapsed) = recorder.Records[0];
        Assert.AreEqual((FrameCostCategory.Draw, "GameLoop", "Shell.Draw"), (category, groupName, itemName));
        Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, elapsed);
    }

    [TestMethod]
    public void FrameCost_OpenedWithNoListener_RecordsNothing_EvenIfOneSubscribesBeforeItEnds()
    {
        var recorder = new FrameCostRecorder();

        using (EngineHooks.FrameCost(FrameCostCategory.Update, "GameLoop", "Shell.Update"))
        {
            using var subscription = EngineHooks.FrameCosts.Subscribe(recorder);
        }

        Assert.IsEmpty(recorder.Records);
    }
}
