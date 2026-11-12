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
        public List<(FrameCostRowName Name, TimeSpan Elapsed)> Records { get; } = [];

        public void Record(FrameCostRowId row, TimeSpan elapsed) => Records.Add((row.Name, elapsed));
    }

    [TestMethod]
    public void FrameCost_Disposed_RecordsOnceWithItsRow()
    {
        var recorder = new FrameCostRecorder();
        using var subscription = EngineHooks.FrameCosts.Subscribe(recorder);

        using (EngineHooks.FrameCost(FrameCostRowIds.Of(FrameCostCategory.Draw, "GameLoop", "Shell.Draw")))
        {
        }

        Assert.HasCount(1, recorder.Records);
        var (name, elapsed) = recorder.Records[0];
        Assert.AreEqual(new FrameCostRowName(FrameCostCategory.Draw, "GameLoop", "Shell.Draw"), name);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, elapsed);
    }

    [TestMethod]
    public void FrameCostRowIds_TheSameNameTwice_IsOneId_AndNamesItBack()
    {
        var first = FrameCostRowIds.Of(FrameCostCategory.Update, "EngineHooksTests", "Row");
        var second = FrameCostRowIds.Of(FrameCostCategory.Update, "EngineHooksTests", new string("Row".AsSpan()));
        var otherCategory = FrameCostRowIds.Of(FrameCostCategory.Draw, "EngineHooksTests", "Row");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, otherCategory);
        Assert.AreEqual(new FrameCostRowName(FrameCostCategory.Update, "EngineHooksTests", "Row"), first.Name);
        Assert.IsTrue(FrameCostRowIds.TryFind(FrameCostCategory.Draw, "EngineHooksTests", "Row", out var found));
        Assert.AreEqual(otherCategory, found);
        Assert.IsFalse(FrameCostRowIds.TryFind(FrameCostCategory.Update, "EngineHooksTests", "Never issued", out _));
    }

    [TestMethod]
    public void FrameCost_OpenedWithNoListener_RecordsNothing_EvenIfOneSubscribesBeforeItEnds()
    {
        var recorder = new FrameCostRecorder();

        using (EngineHooks.FrameCost(FrameCostRowIds.Of(FrameCostCategory.Update, "GameLoop", "Shell.Update")))
        {
            using var subscription = EngineHooks.FrameCosts.Subscribe(recorder);
        }

        Assert.IsEmpty(recorder.Records);
    }
}
