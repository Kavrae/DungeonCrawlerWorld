using Engine.Decisions;
using Engine.ECS.Entities;
using Engine.Tags;
using static Tests.Decisions.DecisionTestKit;

namespace Tests.Decisions;

[TestClass]
public sealed class BehaviorArbiterTests
{
    private static readonly EntityKey Hostile = new(7);

    private static BehaviorDecision Decide(BehaviorArbiter<FakeContext> arbiter, BehaviorSet<FakeContext> set, DecisionLog log, long now, ref BehaviorCommitment commitment, DecisionTrace? trace = null)
    {
        var context = new FakeContext(log, now);
        var decision = arbiter.Decide(ref context, set, commitment, trace);
        if (decision.StartedCommitment)
        {
            commitment = decision.Commitment;
        }

        return decision;
    }

    private static BehaviorDecision ChooseThenAct(BehaviorArbiter<FakeContext> arbiter, BehaviorSet<FakeContext> set, DecisionLog log, long now, ref BehaviorCommitment commitment)
    {
        var chooseContext = new FakeContext(log, now);
        var choice = arbiter.Choose(ref chooseContext, set, commitment);
        var actContext = new FakeContext(log, now);
        var decision = arbiter.Act(ref actContext, set, choice, commitment);
        if (decision.StartedCommitment)
        {
            commitment = decision.Commitment;
        }

        return decision;
    }

    private static IBehavior<FakeContext>? Chosen(BehaviorSet<FakeContext> set, BehaviorDecision decision) =>
        decision.Choice.IsNone ? null : set[decision.Choice.EntryIndex].Behavior;

    [TestMethod]
    public void ABehaviorEvaluatingAboveItsCeiling_Throws()
    {
        var greedy = new FakeBehavior("Greedy").Applying(4, 1f);
        var set = Set(FrozenCatalog(greedy), Grant(greedy, 3));
        var commitment = default(BehaviorCommitment);

        Assert.ThrowsExactly<InvalidOperationException>(() => Decide(Arbiter(), set, new DecisionLog(), 0, ref commitment));
    }

    [TestMethod]
    public void OnceARankApplies_NoBehaviorWithALowerCeilingIsAsked()
    {
        var heal = new FakeBehavior("Heal").Applying(4, 1f);
        var wander = new FakeBehavior("Wander").Applying(1, 1f);
        var set = Set(FrozenCatalog(heal, wander), Grant(heal, 4), Grant(wander, 1));
        var commitment = default(BehaviorCommitment);

        var decision = Decide(Arbiter(), set, new DecisionLog(), 0, ref commitment);

        Assert.AreSame(heal, Chosen(set, decision));
        Assert.AreEqual(0, wander.EvaluateCalls);
        Assert.AreEqual(0, wander.ActCalls);
        Assert.AreEqual(1, heal.ActCalls);
    }

    [TestMethod]
    public void WhenAHigherRankDoesntApply_TheNextRankIsAsked()
    {
        var heal = new FakeBehavior("Heal");
        var wander = new FakeBehavior("Wander").Applying(1, 1f);
        var set = Set(FrozenCatalog(heal, wander), Grant(heal, 4), Grant(wander, 1));
        var commitment = default(BehaviorCommitment);

        var decision = Decide(Arbiter(), set, new DecisionLog(), 0, ref commitment);

        Assert.AreSame(wander, Chosen(set, decision));
        Assert.AreEqual(1, heal.EvaluateCalls);
    }

    [TestMethod]
    public void NothingApplying_ChoosesNothing_AndActsOnNothing()
    {
        var heal = new FakeBehavior("Heal");
        var set = Set(FrozenCatalog(heal), Grant(heal, 4));
        var commitment = default(BehaviorCommitment);

        var decision = Decide(Arbiter(), set, new DecisionLog(), 0, ref commitment);

        Assert.IsTrue(decision.Choice.IsNone);
        Assert.AreEqual(0, heal.ActCalls);
    }

    [TestMethod]
    public void ACandidateUnderTheCutoff_NeverWins_AndTheOneLeftDrawsNothing()
    {
        var strong = new FakeBehavior("Strong").Applying(2, 1f);
        var weak = new FakeBehavior("Weak").Applying(2, 0.2f);
        var set = Set(FrozenCatalog(strong, weak), Grant(strong, 2), Grant(weak, 2));
        var arbiter = Arbiter();
        var log = new DecisionLog();

        for (var trial = 0; trial < 50; trial++)
        {
            var commitment = default(BehaviorCommitment);
            Assert.AreSame(strong, Chosen(set, Decide(arbiter, set, log, trial, ref commitment)));
        }

        Assert.AreEqual(0, log.Draws);
    }

    [TestMethod]
    public void CandidatesAtTheWinningRank_AreDrawnByWeight()
    {
        var often = new FakeBehavior("Often").Applying(2, 3f);
        var seldom = new FakeBehavior("Seldom").Applying(2, 1f);
        var set = Set(FrozenCatalog(often, seldom), Grant(often, 2), Grant(seldom, 2));
        var arbiter = Arbiter();
        var log = new DecisionLog(seed: 1234);
        const int Trials = 4000;

        var oftenWins = 0;
        for (var trial = 0; trial < Trials; trial++)
        {
            var commitment = default(BehaviorCommitment);
            if (ReferenceEquals(Chosen(set, Decide(arbiter, set, log, trial, ref commitment)), often))
            {
                oftenWins++;
            }
        }

        Assert.AreEqual(Trials, log.Draws);
        Assert.IsTrue(oftenWins is > (int)(Trials * 0.72) and < (int)(Trials * 0.78), $"Expected about 75% of {Trials}, got {oftenWins}.");
    }

    [TestMethod]
    public void ALoneCandidate_DrawsNothing()
    {
        var wander = new FakeBehavior("Wander").Applying(1, 1f);
        var idle = new FakeBehavior("Idle");
        var set = Set(FrozenCatalog(wander, idle), Grant(wander, 1), Grant(idle, 1));
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(Arbiter(), set, log, 0, ref commitment);

        Assert.AreEqual(0, log.Draws);
    }

    [TestMethod]
    public void ConsiderationsAndTheRuntimeMultiplier_AreAskedOnlyOnceABehaviorWeighsSomething()
    {
        var heal = new FakeBehavior("Heal");
        var wander = new FakeBehavior("Wander").Applying(1, 1f);
        var healConsideration = new FakeConsideration(2f);
        var wanderConsideration = new FakeConsideration(2f);
        var set = Set(FrozenCatalog(heal, wander), Grant(heal, 4, considerations: healConsideration), Grant(wander, 1, considerations: wanderConsideration));
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(Arbiter(), set, log, 0, ref commitment);

        Assert.AreEqual(0, healConsideration.Calls);
        Assert.AreEqual(1, wanderConsideration.Calls);
        Assert.AreEqual(1, log.RuntimeMultiplierCalls);
    }

    [TestMethod]
    public void AConsiderationOfZero_VetoesItsBehavior_WithoutAskingTheRuntimeMultiplier()
    {
        var vetoed = new FakeBehavior("Vetoed").Applying(2, 10f);
        var other = new FakeBehavior("Other").Applying(2, 1f);
        var set = Set(FrozenCatalog(vetoed, other), Grant(vetoed, 2, considerations: new FakeConsideration(0f)), Grant(other, 2));
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        var decision = Decide(Arbiter(), set, log, 0, ref commitment);

        Assert.AreSame(other, Chosen(set, decision));
        Assert.AreEqual(1, log.RuntimeMultiplierCalls);
        Assert.AreEqual(0, log.Draws);
    }

    [TestMethod]
    public void TheRuntimeMultiplier_WeighsABehaviorByItsTags()
    {
        var flee = new FakeBehavior("Flee", [Caution]).Applying(3, 1f);
        var engage = new FakeBehavior("Engage", [Aggression]).Applying(3, 1f);
        var set = Set(FrozenCatalog(flee, engage), Grant(flee, 3), Grant(engage, 3));
        var log = new DecisionLog();
        log.RuntimeMultipliers[Caution] = 10f;
        var commitment = default(BehaviorCommitment);

        Assert.AreSame(flee, Chosen(set, Decide(Arbiter(), set, log, 0, ref commitment)));
    }

    [TestMethod]
    public void ACommitment_IsKeptAgainstRivalsOfItsRank_WithNoDraw()
    {
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var engage = new FakeBehavior("Engage");
        var set = Set(FrozenCatalog(flee, engage), Grant(flee, 3, commitFrames: 30), Grant(engage, 3));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        var first = Decide(arbiter, set, log, 0, ref commitment);
        engage.Applying(3, 1000f);
        var engageCallsBefore = engage.EvaluateCalls;
        var trace = new DecisionTrace();
        var second = Decide(arbiter, set, log, 10, ref commitment, trace);

        Assert.IsTrue(first.StartedCommitment);
        Assert.AreEqual(30u, commitment.CommittedUntilFrame);
        Assert.AreSame(flee, Chosen(set, second));
        Assert.IsTrue(second.Choice.ContinuesCommitment);
        Assert.IsFalse(second.StartedCommitment);
        Assert.AreEqual(engageCallsBefore, engage.EvaluateCalls);
        Assert.AreEqual(0, log.Draws);
        Assert.AreEqual(DecisionPath.Continued, trace.Path);
    }

    [TestMethod]
    public void AHigherRank_InterruptsACommitment_WithoutAskingTheCommittedBehavior()
    {
        var heal = new FakeBehavior("Heal");
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var set = Set(FrozenCatalog(heal, flee), Grant(heal, 4), Grant(flee, 3, commitFrames: 30));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(arbiter, set, log, 0, ref commitment);
        heal.Applying(4, 1f);
        var fleeCallsBefore = flee.EvaluateCalls;
        var trace = new DecisionTrace();
        var decision = Decide(arbiter, set, log, 10, ref commitment, trace);

        Assert.AreSame(heal, Chosen(set, decision));
        Assert.IsFalse(decision.Choice.ContinuesCommitment);
        Assert.AreEqual(fleeCallsBefore, flee.EvaluateCalls);
        Assert.AreEqual(DecisionPath.Interrupt, trace.Path);
    }

    [TestMethod]
    public void ACommitmentWhoseBehaviorNoLongerApplies_EndsEarly_IntoAFreshPick()
    {
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var engage = new FakeBehavior("Engage").Applying(3, 0.01f);
        var set = Set(FrozenCatalog(flee, engage), Grant(flee, 3, commitFrames: 30), Grant(engage, 3));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(arbiter, set, log, 0, ref commitment);
        flee.Evaluation = _ => BehaviorEvaluation.DoesNotApply;
        var trace = new DecisionTrace();
        var decision = Decide(arbiter, set, log, 10, ref commitment, trace);

        Assert.AreSame(engage, Chosen(set, decision));
        Assert.IsFalse(decision.Choice.ContinuesCommitment);
        Assert.AreEqual(DecisionPath.FreshPick, trace.Path);
    }

    [TestMethod]
    public void ACommitmentWhoseBehaviorNoLongerApplies_AsksEachHigherRankedBehaviorOnce_AndTracesItOnce()
    {
        var heal = new FakeBehavior("Heal");
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var engage = new FakeBehavior("Engage").Applying(3, 1f);
        var set = Set(FrozenCatalog(heal, flee, engage), Grant(heal, 4), Grant(flee, 3, weight: 1000f, commitFrames: 30), Grant(engage, 3));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(arbiter, set, log, 0, ref commitment);
        flee.Evaluation = _ => BehaviorEvaluation.DoesNotApply;
        var healCallsBefore = heal.EvaluateCalls;
        var trace = new DecisionTrace();
        var decision = Decide(arbiter, set, log, 10, ref commitment, trace);

        Assert.AreSame(engage, Chosen(set, decision));
        Assert.AreEqual(healCallsBefore + 1, heal.EvaluateCalls, "The fresh pick reads the interrupt pass's answer rather than asking again.");
        Assert.AreEqual(1, trace.Entries.Count(entry => entry.BehaviorName == "Heal"));
    }

    [TestMethod]
    public void ACommitment_EndsAtItsDeadline()
    {
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var engage = new FakeBehavior("Engage");
        var set = Set(FrozenCatalog(flee, engage), Grant(flee, 3, commitFrames: 30), Grant(engage, 3));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        Decide(arbiter, set, log, 0, ref commitment);
        engage.Applying(3, 1000f);

        Assert.AreSame(flee, Chosen(set, Decide(arbiter, set, log, 29, ref commitment)));
        Assert.AreSame(engage, Chosen(set, Decide(arbiter, set, log, 30, ref commitment)));
    }

    [TestMethod]
    public void ACommitmentToABehaviorTheSetDoesntHold_IsIgnored()
    {
        var gone = new FakeBehavior("Gone");
        var wander = new FakeBehavior("Wander").Applying(1, 1f);
        var catalog = FrozenCatalog(gone, wander);
        var set = Set(catalog, Grant(wander, 1));
        var commitment = new BehaviorCommitment(catalog.SessionIdOf(gone.Id), committedUntilFrame: 100, Hostile);
        var trace = new DecisionTrace();

        var decision = Decide(Arbiter(), set, new DecisionLog(), 0, ref commitment, trace);

        Assert.AreSame(wander, Chosen(set, decision));
        Assert.AreEqual(0, gone.EvaluateCalls);
        Assert.AreEqual(DecisionPath.FreshPick, trace.Path);
    }

    [TestMethod]
    public void TheTargetActLeaves_IsStored_HandedBackOnEachContinue_AndClearedForAFreshPick()
    {
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        flee.TargetToCommitTo = Hostile;
        var engage = new FakeBehavior("Engage");
        var set = Set(FrozenCatalog(flee, engage), Grant(flee, 3, commitFrames: 30), Grant(engage, 2));
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);

        var first = Decide(arbiter, set, log, 0, ref commitment);
        flee.TargetToCommitTo = EntityKey.None;
        Decide(arbiter, set, log, 10, ref commitment);
        Decide(arbiter, set, log, 20, ref commitment);
        flee.Evaluation = target => target == Hostile ? BehaviorEvaluation.DoesNotApply : new BehaviorEvaluation(3, 1f);
        engage.Applying(2, 1f);
        Decide(arbiter, set, log, 25, ref commitment);

        Assert.AreEqual(Hostile, first.Commitment.Target);
        CollectionAssert.AreEqual(new[] { EntityKey.None, Hostile, Hostile, Hostile, EntityKey.None }, flee.TargetsSeenByEvaluate);
        CollectionAssert.AreEqual(new[] { EntityKey.None, Hostile, Hostile, EntityKey.None }, flee.TargetsSeenByAct);
        Assert.IsTrue(engage.TargetsSeenByEvaluate.All(target => target.IsNone));
    }

    [TestMethod]
    public void Choose_CallsNoAct()
    {
        var heal = new FakeBehavior("Heal").Applying(4, 1f);
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        var set = Set(FrozenCatalog(heal, flee), Grant(heal, 4), Grant(flee, 3));
        var context = new FakeContext(new DecisionLog(), 0);

        var choice = Arbiter().Choose(ref context, set, default);

        Assert.IsFalse(choice.IsNone);
        Assert.AreEqual(0, heal.ActCalls);
        Assert.AreEqual(0, flee.ActCalls);
    }

    [TestMethod]
    public void ChooseThenAct_GivesWhatDecideGives_ForAFreshPickAnInterruptAndAContinue()
    {
        var (decideSet, decideBehaviors) = CommittingWorld();
        var (twoPassSet, twoPassBehaviors) = CommittingWorld();
        var decideArbiter = Arbiter();
        var twoPassArbiter = Arbiter();
        var decideLog = new DecisionLog(seed: 5);
        var twoPassLog = new DecisionLog(seed: 5);
        var decideCommitment = default(BehaviorCommitment);
        var twoPassCommitment = default(BehaviorCommitment);

        var decisions = new List<(BehaviorDecision Decide, BehaviorDecision TwoPass)>();
        foreach (var (frame, healApplies) in new[] { (0L, false), (10L, false), (20L, true) })
        {
            decideBehaviors.Heal.Evaluation = _ => healApplies ? new BehaviorEvaluation(4, 1f) : BehaviorEvaluation.DoesNotApply;
            twoPassBehaviors.Heal.Evaluation = decideBehaviors.Heal.Evaluation;
            decisions.Add((Decide(decideArbiter, decideSet, decideLog, frame, ref decideCommitment), ChooseThenAct(twoPassArbiter, twoPassSet, twoPassLog, frame, ref twoPassCommitment)));
        }

        Assert.IsTrue(decisions[0].Decide.StartedCommitment);
        Assert.IsTrue(decisions[1].Decide.Choice.ContinuesCommitment);
        Assert.AreEqual(0, decisions[2].Decide.Choice.EntryIndex);
        foreach (var (decide, twoPass) in decisions)
        {
            Assert.AreEqual(decide, twoPass);
        }

        Assert.AreEqual(decideBehaviors.Flee.ActCalls, twoPassBehaviors.Flee.ActCalls);
        Assert.AreEqual(decideBehaviors.Heal.ActCalls, twoPassBehaviors.Heal.ActCalls);
    }

    [TestMethod]
    public void ATrace_RecordsEveryEvaluatedBehavior_AndWhatBecameOfIt()
    {
        var heal = new FakeBehavior("Heal");
        var engage = new FakeBehavior("Engage", [Aggression]).Applying(3, 1f);
        var flee = new FakeBehavior("Flee", [Caution]).Applying(3, 0.1f);
        var outranked = new FakeBehavior("Outranked").Applying(2, 5f);
        var idle = new FakeBehavior("Idle").Applying(1, 1f);
        var set = Set(FrozenCatalog(heal, engage, flee, outranked, idle), Grant(heal, 4), Grant(outranked, 3), Grant(engage, 3, weight: 2f), Grant(flee, 3), Grant(idle, 1));
        var trace = new DecisionTrace();
        var commitment = default(BehaviorCommitment);

        Decide(Arbiter(), set, new DecisionLog(), 12, ref commitment, trace);

        Assert.AreEqual(12, trace.Frame);
        Assert.AreEqual(DecisionPath.FreshPick, trace.Path);
        var outcomes = trace.Entries.ToDictionary(entry => entry.BehaviorName, entry => entry.Outcome);
        Assert.AreEqual(DecisionTraceOutcome.DidNotApply, outcomes["Heal"]);
        Assert.AreEqual(DecisionTraceOutcome.Outranked, outcomes["Outranked"]);
        Assert.AreEqual(DecisionTraceOutcome.Chosen, outcomes["Engage"]);
        Assert.AreEqual(DecisionTraceOutcome.BelowCutoff, outcomes["Flee"]);
        Assert.IsFalse(outcomes.ContainsKey("Idle"));
        var engageEntry = trace.Entries.Single(entry => entry.BehaviorName == "Engage");
        Assert.AreEqual(2f, engageEntry.ResolvedWeight);
        Assert.AreEqual(2f, engageEntry.FinalWeight);
    }

    [TestMethod]
    public void Deciding_AllocatesNothing()
    {
        var engage = new QuietBehavior(3, 1f);
        var flee = new QuietBehavior(3, 1f);
        var idle = new QuietBehavior(1, 1f);
        var catalog = new BehaviorCatalog<FakeContext>();
        catalog.Register(engage);
        catalog.Register(flee);
        catalog.Register(idle);
        catalog.Freeze(DecisionTags, BehaviorDomain);
        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[new(engage.Id, 3, Considerations: [new FakeConsideration(2f)]), new(flee.Id, 3, CommitFrames: 5), new(idle.Id, 1)]], []);
        var arbiter = Arbiter();
        var log = new DecisionLog();
        var commitment = default(BehaviorCommitment);
        for (var frame = 0; frame < 100; frame++)
        {
            Decide(arbiter, set, log, frame, ref commitment);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 100; frame < 1100; frame++)
        {
            Decide(arbiter, set, log, frame, ref commitment);
        }

        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (BehaviorSet<FakeContext> Set, (FakeBehavior Heal, FakeBehavior Flee) Behaviors) CommittingWorld()
    {
        var heal = new FakeBehavior("Heal");
        var flee = new FakeBehavior("Flee").Applying(3, 1f);
        flee.TargetToCommitTo = Hostile;
        var engage = new FakeBehavior("Engage").Applying(3, 0.4f);
        var set = Set(FrozenCatalog(heal, flee, engage), Grant(heal, 4), Grant(flee, 3, weight: 2f, commitFrames: 30), Grant(engage, 3));
        return (set, (heal, flee));
    }

    /// <summary>A behavior that always applies at one rank and weight and records nothing, for measuring what the arbiter itself allocates.</summary>
    private sealed class QuietBehavior(byte rank, float weight) : IBehavior<FakeContext>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Name => "Quiet";

        public GameplayTagSet Tags => GameplayTagSet.Empty;

        public BehaviorEvaluation Evaluate(ref FakeContext context, BehaviorGrant<FakeContext> grant) => new(rank, weight);

        public void Act(ref FakeContext context, BehaviorGrant<FakeContext> grant)
        {
        }
    }
}
