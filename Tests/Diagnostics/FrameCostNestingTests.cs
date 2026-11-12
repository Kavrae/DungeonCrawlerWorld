using Engine.Diagnostics;
using Engine.Events;

namespace Tests.Diagnostics;

/// <summary>Frame costs nest: every emit site opens its measurement before what it measures and closes it after, so a recorder knows what each ran inside.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FrameCostNestingTests
{
    private static readonly TimeSpan PastOneWindow = TimeSpan.FromSeconds(1.05);

    private static readonly FrameCostRowId Frame = Row(FrameCostCategory.Update, "GameLoop", "Frame");
    private static readonly FrameCostRowId ShellDraw = Row(FrameCostCategory.Draw, "GameLoop", "Shell.Draw");
    private static readonly FrameCostRowId AuraSystem = Row(FrameCostCategory.Update, "SystemManager", "AuraSystem");
    private static readonly FrameCostRowId DeathSystem = Row(FrameCostCategory.Update, "SystemManager", "DeathSystem");
    private static readonly FrameCostRowId MovementSystem = Row(FrameCostCategory.Update, "SystemManager", "MovementSystem");
    private static readonly FrameCostRowId AuraSourceRemovedEvent = Row(FrameCostCategory.Update, "EventBus", "AuraSourceRemovedEvent");
    private static readonly FrameCostRowId EntityDiedEvent = Row(FrameCostCategory.Update, "EventBus", "EntityDiedEvent");

    private readonly record struct TestEvent(int Value);

    private static FrameCostRowId Row(FrameCostCategory category, string groupName, string itemName) => FrameCostRowIds.Of(category, groupName, itemName);

    private static TimeSpan Milliseconds(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private sealed class CallRecorder : IFrameCostRecorder
    {
        public List<string> Calls { get; } = [];

        public void Begin(FrameCostRowId row) => Calls.Add($"Begin {row}");

        public void Record(FrameCostRowId row, TimeSpan elapsed) => Calls.Add($"Record {row}");
    }

    [TestMethod]
    public void AnEventPublishedInsideAMeasurement_IsOpenedAndClosedInsideIt()
    {
        var recorder = new CallRecorder();
        var eventBus = new EventBus();
        eventBus.Subscribe<TestEvent>(static _ => { });
        using (EngineHooks.FrameCosts.Subscribe(recorder))
        {
            using (EngineHooks.FrameCost(Row(FrameCostCategory.Update, "GameLoop", "Shell.Update")))
            {
                eventBus.Publish(new TestEvent(1));
            }
        }

        CollectionAssert.AreEqual(
            new[] { "Begin GameLoop.Shell.Update", "Begin EventBus.TestEvent", "Record EventBus.TestEvent", "Record GameLoop.Shell.Update" },
            recorder.Calls);
    }

    [TestMethod]
    public void TheTracker_GivesEachEntryWhatItRanDirectlyInside()
    {
        var tracker = new FrameBudgetTracker();
        tracker.Begin(Frame);
        tracker.Begin(AuraSystem);
        tracker.Begin(AuraSourceRemovedEvent);
        tracker.Record(AuraSourceRemovedEvent, Milliseconds(1));
        tracker.Record(AuraSystem, Milliseconds(3));
        tracker.Begin(MovementSystem);
        tracker.Record(MovementSystem, Milliseconds(2));
        tracker.Record(Frame, Milliseconds(6));
        tracker.Record(ShellDraw, Milliseconds(4));

        Thread.Sleep(PastOneWindow);
        tracker.Record(ShellDraw, Milliseconds(4));

        var entries = tracker.Snapshot.ToDictionary(static entry => entry.ItemName);
        Assert.IsNull(entries["Frame"].Parents);
        CollectionAssert.AreEqual(new[] { Frame.Name }, entries["AuraSystem"].Parents!.ToArray());
        CollectionAssert.AreEqual(new[] { Frame.Name }, entries["MovementSystem"].Parents!.ToArray());
        CollectionAssert.AreEqual(new[] { AuraSystem.Name }, entries["AuraSourceRemovedEvent"].Parents!.ToArray());
        Assert.IsNull(entries["Shell.Draw"].Parents);
    }

    [TestMethod]
    public void AnEntryRecordedUnderSeveralParents_HasEachOnce()
    {
        var tracker = new FrameBudgetTracker();
        foreach (var system in new[] { AuraSystem, DeathSystem, AuraSystem })
        {
            tracker.Begin(system);
            tracker.Begin(EntityDiedEvent);
            tracker.Record(EntityDiedEvent, Milliseconds(1));
            tracker.Record(system, Milliseconds(1));
        }

        Thread.Sleep(PastOneWindow);
        tracker.Record(ShellDraw, Milliseconds(1));

        var parents = tracker.Snapshot.Single(static entry => entry.ItemName == "EntityDiedEvent").Parents!;
        CollectionAssert.AreEquivalent(new[] { "AuraSystem", "DeathSystem" }, parents.Select(static parent => parent.ItemName).ToArray());
    }

    [TestMethod]
    public void AnEntryNestedInItself_IsParentedWhereTheOuterOneIs_AndCountsOnlyTheOuterOnesTime()
    {
        var tracker = new FrameBudgetTracker();
        tracker.Begin(DeathSystem);
        tracker.Begin(EntityDiedEvent);
        tracker.Begin(EntityDiedEvent);
        tracker.Record(EntityDiedEvent, Milliseconds(1));
        tracker.Record(EntityDiedEvent, Milliseconds(2));
        tracker.Record(DeathSystem, Milliseconds(3));

        Thread.Sleep(PastOneWindow);
        tracker.Record(ShellDraw, Milliseconds(1));

        var entityDied = tracker.TakeIntervalSnapshot(out var intervalSeconds).Single(static entry => entry.ItemName == "EntityDiedEvent");
        CollectionAssert.AreEqual(new[] { DeathSystem.Name }, entityDied.Parents!.ToArray());
        Assert.AreEqual(2, entityDied.MillisecondsPerSecond * intervalSeconds, 1e-6);
    }

    [TestMethod]
    public void TheWindow_ClosesOnlyOnceNoMeasurementIsOpen()
    {
        var tracker = new FrameBudgetTracker();
        tracker.Begin(Frame);
        Thread.Sleep(PastOneWindow);
        tracker.Begin(AuraSystem);
        tracker.Record(AuraSystem, Milliseconds(1));

        Assert.IsEmpty(tracker.Snapshot);

        tracker.Record(Frame, Milliseconds(1100));

        CollectionAssert.AreEquivalent(new[] { "Frame", "AuraSystem" }, tracker.Snapshot.Select(static entry => entry.ItemName).ToArray());
    }

    [TestMethod]
    public void TheIntervalSnapshot_HoldsEveryWindowSinceTheLastOne_PerSecondOfThem_ThenStartsAgain()
    {
        var tracker = new FrameBudgetTracker();
        tracker.Record(ShellDraw, Milliseconds(4));
        Thread.Sleep(PastOneWindow);
        tracker.Record(ShellDraw, Milliseconds(2));
        var firstWindow = tracker.Snapshot.Single();
        Thread.Sleep(PastOneWindow);
        tracker.Record(ShellDraw, Milliseconds(1));
        var secondWindow = tracker.Snapshot.Single();

        var interval = tracker.TakeIntervalSnapshot(out var intervalSeconds).Single();

        Assert.IsGreaterThanOrEqualTo(2 * PastOneWindow.TotalSeconds, intervalSeconds);
        Assert.AreEqual(7, interval.MillisecondsPerSecond * intervalSeconds, 1e-6);
        Assert.IsLessThan(6, firstWindow.MillisecondsPerSecond);
        Assert.IsLessThan(1, secondWindow.MillisecondsPerSecond);
        Assert.IsEmpty(tracker.TakeIntervalSnapshot(out var emptyIntervalSeconds));
        Assert.AreEqual(0, emptyIntervalSeconds);
    }

    [TestMethod]
    public void ParentsSurviveAReport_WrittenAndReadBack()
    {
        FrameCostEntry[] costs = [new(FrameCostCategory.Update, "GameLoop", "Frame", 6), new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 3, [Frame.Name])];

        var report = DiagnosticsReportReader.ReadPeriodicReport(System.Text.Encoding.UTF8.GetBytes(DiagnosticsReportWriter.Serialize(
            DiagnosticsReportWriter.Build(DateTime.UtcNow, DiagnosticsFeatures.FrameBudget, 1, costs, null, null, null))));

        var read = DiagnosticsReportReader.FrameCostsOf(report).ToDictionary(static entry => entry.ItemName);
        Assert.IsNull(read["Frame"].Parents);
        CollectionAssert.AreEqual(new[] { Frame.Name }, read["AuraSystem"].Parents!.ToArray());
    }
}
