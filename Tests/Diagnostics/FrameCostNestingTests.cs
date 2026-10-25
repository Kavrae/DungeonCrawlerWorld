using Engine.Diagnostics;
using Engine.Events;

namespace Tests.Diagnostics;

/// <summary>Frame costs nest: every emit site opens its measurement before what it measures and closes it after, so a recorder knows what each ran inside.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FrameCostNestingTests
{
    private readonly record struct TestEvent(int Value);

    private sealed class CallRecorder : IFrameCostRecorder
    {
        public List<string> Calls { get; } = [];

        public void Begin(FrameCostCategory category, string groupName, string itemName) => Calls.Add($"Begin {groupName}.{itemName}");

        public void Record(FrameCostCategory category, string groupName, string itemName, TimeSpan elapsed) => Calls.Add($"Record {groupName}.{itemName}");
    }

    [TestMethod]
    public void AnEventPublishedInsideAMeasurement_IsOpenedAndClosedInsideIt()
    {
        var recorder = new CallRecorder();
        var eventBus = new EventBus();
        eventBus.Subscribe<TestEvent>(static _ => { });
        using (EngineHooks.FrameCosts.Subscribe(recorder))
        {
            using (EngineHooks.FrameCost(FrameCostCategory.Update, "GameLoop", "Shell.Update"))
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
        tracker.Begin(FrameCostCategory.Update, "GameLoop", "Frame");
        tracker.Begin(FrameCostCategory.Update, "SystemManager", "AuraSystem");
        tracker.Begin(FrameCostCategory.Update, "EventBus", "AuraSourceRemovedEvent");
        tracker.Record(FrameCostCategory.Update, "EventBus", "AuraSourceRemovedEvent", TimeSpan.FromMilliseconds(1));
        tracker.Record(FrameCostCategory.Update, "SystemManager", "AuraSystem", TimeSpan.FromMilliseconds(3));
        tracker.Begin(FrameCostCategory.Update, "SystemManager", "MovementSystem");
        tracker.Record(FrameCostCategory.Update, "SystemManager", "MovementSystem", TimeSpan.FromMilliseconds(2));
        tracker.Record(FrameCostCategory.Update, "GameLoop", "Frame", TimeSpan.FromMilliseconds(6));
        tracker.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", TimeSpan.FromMilliseconds(4));

        Thread.Sleep(TimeSpan.FromSeconds(1.05));
        tracker.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", TimeSpan.FromMilliseconds(4));

        var entries = tracker.Snapshot.ToDictionary(static entry => entry.ItemName);
        FrameCostRowName frame = new(FrameCostCategory.Update, "GameLoop", "Frame");
        Assert.IsNull(entries["Frame"].Parents);
        CollectionAssert.AreEqual(new[] { frame }, entries["AuraSystem"].Parents!.ToArray());
        CollectionAssert.AreEqual(new[] { frame }, entries["MovementSystem"].Parents!.ToArray());
        CollectionAssert.AreEqual(new[] { new FrameCostRowName(FrameCostCategory.Update, "SystemManager", "AuraSystem") }, entries["AuraSourceRemovedEvent"].Parents!.ToArray());
        Assert.IsNull(entries["Shell.Draw"].Parents);
    }

    [TestMethod]
    public void AnEntryRecordedUnderSeveralParents_HasEachOnce()
    {
        var tracker = new FrameBudgetTracker();
        foreach (var system in new[] { "AuraSystem", "DeathSystem", "AuraSystem" })
        {
            tracker.Begin(FrameCostCategory.Update, "SystemManager", system);
            tracker.Begin(FrameCostCategory.Update, "EventBus", "EntityDiedEvent");
            tracker.Record(FrameCostCategory.Update, "EventBus", "EntityDiedEvent", TimeSpan.FromMilliseconds(1));
            tracker.Record(FrameCostCategory.Update, "SystemManager", system, TimeSpan.FromMilliseconds(1));
        }

        Thread.Sleep(TimeSpan.FromSeconds(1.05));
        tracker.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", TimeSpan.FromMilliseconds(1));

        var parents = tracker.Snapshot.Single(static entry => entry.ItemName == "EntityDiedEvent").Parents!;
        CollectionAssert.AreEquivalent(new[] { "AuraSystem", "DeathSystem" }, parents.Select(static parent => parent.ItemName).ToArray());
    }

    [TestMethod]
    public void AnEntryNestedInItself_IsParentedWhereTheOuterOneIs_NeverUnderItself()
    {
        var tracker = new FrameBudgetTracker();
        tracker.Begin(FrameCostCategory.Update, "SystemManager", "DeathSystem");
        tracker.Begin(FrameCostCategory.Update, "EventBus", "EntityDiedEvent");
        tracker.Begin(FrameCostCategory.Update, "EventBus", "EntityDiedEvent");
        tracker.Record(FrameCostCategory.Update, "EventBus", "EntityDiedEvent", TimeSpan.FromMilliseconds(1));
        tracker.Record(FrameCostCategory.Update, "EventBus", "EntityDiedEvent", TimeSpan.FromMilliseconds(2));
        tracker.Record(FrameCostCategory.Update, "SystemManager", "DeathSystem", TimeSpan.FromMilliseconds(3));

        Thread.Sleep(TimeSpan.FromSeconds(1.05));
        tracker.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", TimeSpan.FromMilliseconds(1));

        var parents = tracker.Snapshot.Single(static entry => entry.ItemName == "EntityDiedEvent").Parents!;
        CollectionAssert.AreEqual(new[] { new FrameCostRowName(FrameCostCategory.Update, "SystemManager", "DeathSystem") }, parents.ToArray());
    }

    [TestMethod]
    public void ParentsSurviveAReport_WrittenAndReadBack()
    {
        FrameCostRowName frame = new(FrameCostCategory.Update, "GameLoop", "Frame");
        FrameCostEntry[] costs = [new(FrameCostCategory.Update, "GameLoop", "Frame", 6), new(FrameCostCategory.Update, "SystemManager", "AuraSystem", 3, [frame])];

        var report = DiagnosticsReportReader.ReadPeriodicReport(System.Text.Encoding.UTF8.GetBytes(DiagnosticsReportWriter.Serialize(
            DiagnosticsReportWriter.Build(DateTime.UtcNow, DiagnosticsFeatures.FrameBudget, 1, costs, null, null, null))));

        var read = DiagnosticsReportReader.FrameCostsOf(report).ToDictionary(static entry => entry.ItemName);
        Assert.IsNull(read["Frame"].Parents);
        CollectionAssert.AreEqual(new[] { frame }, read["AuraSystem"].Parents!.ToArray());
    }
}
