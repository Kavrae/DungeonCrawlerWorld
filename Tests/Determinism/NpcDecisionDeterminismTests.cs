using Game.Modules.NpcBehavior;

namespace Tests.Determinism;

/// <summary>What makes NPC decisions safe to run on worker threads: a seed decides everything, and choosing reads only, so no creature's choice depends on who chose before it.</summary>
[TestClass]
[TestCategory("Determinism")]
public sealed class NpcDecisionDeterminismTests
{
    private static void RunWholeScenario(NpcDeterminismScenario scenario, Action? afterEachSample = null)
    {
        for (var sample = 0; sample < NpcDeterminismScenario.SampleCount; sample++)
        {
            scenario.RunFrames(NpcDeterminismScenario.FramesBetweenSamples);
            afterEachSample?.Invoke();
        }
    }

    [TestMethod]
    public void TheScenario_ReachesEveryBuiltInBehaviorAndAConsideration()
    {
        var scenario = new NpcDeterminismScenario();

        RunWholeScenario(scenario);

        var neverApplied = NpcBehaviorModule.BuiltInBehaviors.Where(behavior => !scenario.BehaviorsThatApplied.Contains(behavior.Id)).Select(static behavior => behavior.Name).ToList();
        var neverActedOn = NpcBehaviorModule.BuiltInBehaviors.Where(behavior => !scenario.BehaviorsActedOn.Contains(behavior.Id)).Select(static behavior => behavior.Name).ToList();
        Assert.IsEmpty(neverApplied, $"Never applied: {string.Join(", ", neverApplied)}");
        Assert.IsEmpty(neverActedOn, $"Never acted on: {string.Join(", ", neverActedOn)}");
        Assert.IsTrue(scenario.AskedAConsideration);
    }

    [TestMethod]
    public void TheSameSeed_GivesTheSameTraceAndWorld()
    {
        var first = new NpcDeterminismScenario();
        var second = new NpcDeterminismScenario();

        RunWholeScenario(first);
        RunWholeScenario(second);

        Assert.IsNotEmpty(first.Trace.Entries);
        SimulationTrace.AssertSame(first.Trace, second.Trace);
        CollectionAssert.AreEqual(first.EndState(), second.EndState());
    }

    [TestMethod]
    public void ADifferentSeed_GivesADifferentTrace()
    {
        var first = new NpcDeterminismScenario();
        var second = new NpcDeterminismScenario(NpcDeterminismScenario.DefaultSessionSeed + 1);

        RunWholeScenario(first);
        RunWholeScenario(second);

        Assert.IsFalse(first.Trace.Entries.SequenceEqual(second.Trace.Entries));
    }

    [TestMethod]
    public void EachCreaturesChoice_IsTheSameWhicheverOrderTheyChooseIn()
    {
        var scenario = new NpcDeterminismScenario();
        var compared = 0;

        RunWholeScenario(scenario, () =>
        {
            var forward = scenario.ChooseForAll(inReverseOrder: false);
            var reversed = scenario.ChooseForAll(inReverseOrder: true);

            Assert.HasCount(forward.Count, reversed);
            foreach (var (entityKey, choice) in forward)
            {
                Assert.AreEqual(choice, reversed[entityKey], $"Creature {entityKey} chose differently with the others choosing in reverse.");
            }

            compared += forward.Count;
        });

        Assert.IsGreaterThan(100, compared, "The scenario keeps creatures free to decide.");
    }

    [TestMethod]
    public void Choosing_WritesNoComponent()
    {
        var scenario = new NpcDeterminismScenario();

        RunWholeScenario(scenario, () =>
        {
            var before = scenario.EveryComponentVersion();

            scenario.ChooseForAll(inReverseOrder: false);

            CollectionAssert.AreEqual(before, scenario.EveryComponentVersion());
        });
    }

    [TestMethod]
    public void ChoosingAnExtraTime_ChangesNothingThatFollows()
    {
        var plain = new NpcDeterminismScenario();
        var choosingExtra = new NpcDeterminismScenario();

        RunWholeScenario(plain);
        RunWholeScenario(choosingExtra, () => choosingExtra.ChooseForAll(inReverseOrder: false));

        SimulationTrace.AssertSame(plain.Trace, choosingExtra.Trace);
        CollectionAssert.AreEqual(plain.EndState(), choosingExtra.EndState());
        CollectionAssert.AreEqual(plain.NextValueOfEachGameplayStream(), choosingExtra.NextValueOfEachGameplayStream());
    }
}
