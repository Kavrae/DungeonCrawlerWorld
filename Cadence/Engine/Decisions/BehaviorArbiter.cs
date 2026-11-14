using Engine.ECS.Entities;
using Engine.ECS.Systems;

namespace Engine.Decisions;

/// <summary>The game's settings for its arbiters.</summary>
/// <param name="CutoffFraction">A candidate weighing less than this share of the best weight at the winning rank never wins a roll.</param>
public sealed record BehaviorArbiterOptions(float CutoffFraction = 0.25f);

/// <summary>What Choose picked: the entry's place in the set (or none), the rank it applied at, and whether it continues the live commitment.</summary>
/// <remarks>A plain value, so a caller can choose for a whole batch of entities before any of them acts.</remarks>
public readonly record struct BehaviorChoice(short EntryIndex, byte Rank, bool ContinuesCommitment)
{
    public static BehaviorChoice None => new(-1, 0, false);

    public bool IsNone => EntryIndex < 0;
}

/// <summary>What Act did: the choice it carried out, and the commitment it started, if any.</summary>
/// <param name="StartedCommitment">Whether Commitment is new and should replace the entity's stored one.</param>
public readonly record struct BehaviorDecision(BehaviorChoice Choice, bool StartedCommitment, BehaviorCommitment Commitment)
{
    public static BehaviorDecision None => new(BehaviorChoice.None, false, default);
}

/// <summary>Chooses what an entity does from its behavior set by dual utility, and keeps a pick it committed to.</summary>
/// <remarks>
/// Dual utility (rank, then weight): the highest rank at which anything applies wins outright, and the behaviors at that
/// rank are drawn between by weight, any under the cutoff's share of the best left out. Each behavior's weight is its
/// intrinsic weight from Evaluate, times its grant's weight after modifiers, times its grant's considerations, times the
/// context's runtime multiplier for its tags; the last two are asked only once it already weighs something. A grant's
/// rank is a ceiling, so once a rank has applied, every behavior whose ceiling is lower is never asked. A lone candidate
/// draws nothing.
///
/// A live commitment (BehaviorCommitment) is kept against everything at its rank and below: only behaviors ranked above
/// it are asked first, and if none applies the committed behavior is asked again -- with its target handed back -- and
/// kept, with no draw against its rivals, while it applies. When it no longer applies, a fresh pick follows.
///
/// Choose reads and returns a plain choice; Act carries one out. A caller with many entities chooses for all of them
/// before any acts, so no choice depends on another entity's act. The arbiter holds no pool and no ref into one: the
/// caller stores the commitment Act returns. One arbiter per caller (its candidate buffer is reused), never shared
/// between threads; it allocates nothing per decision.
/// </remarks>
public sealed class BehaviorArbiter<TContext>(BehaviorArbiterOptions options)
    where TContext : IDecisionContext, allows ref struct
{
    private readonly List<(int EntryIndex, byte Rank, float Weight)> _candidates = new(16);
    private readonly List<BehaviorEvaluation> _interruptEvaluations = new(16);

    /// <summary>Chooses what context's entity does from set, given its stored commitment; writes nothing and calls no Act.</summary>
    /// <param name="trace">Filled with every behavior evaluated, when given.</param>
    /// <exception cref="InvalidOperationException">A behavior evaluated at a rank above its grant's ceiling.</exception>
    public BehaviorChoice Choose(ref TContext context, BehaviorSet<TContext> set, in BehaviorCommitment commitment, DecisionTrace? trace = null)
    {
        trace?.Begin(context.Now);
        context.CommitmentTarget = EntityKey.None;

        _interruptEvaluations.Clear();
        var committedIndex = commitment.IsLiveAt(context.Now) ? set.IndexOf(commitment.BehaviorSessionId) : -1;
        if (committedIndex >= 0)
        {
            var interrupt = ChooseAmong(ref context, set, set[committedIndex].Rank, trace, _interruptEvaluations);
            if (!interrupt.IsNone)
            {
                SetPath(trace, DecisionPath.Interrupt);
                return interrupt;
            }

            context.CommitmentTarget = commitment.Target;
            ref readonly var committed = ref set[committedIndex];
            var evaluation = Evaluate(ref context, committed);
            var continues = evaluation.Rank > 0 && evaluation.Weight > 0;
            trace?.Add(new DecisionTraceEntry(committedIndex, committed.Behavior.Name, committed.Rank, evaluation.Rank, evaluation.Weight, committed.ResolvedWeight, 1f, 1f, evaluation.Weight,
                IsCommitted: true, continues ? DecisionTraceOutcome.Chosen : DecisionTraceOutcome.DidNotApply));
            if (continues)
            {
                SetPath(trace, DecisionPath.Continued);
                return new BehaviorChoice((short)committedIndex, evaluation.Rank, ContinuesCommitment: true);
            }

            context.CommitmentTarget = EntityKey.None;
            trace?.RemoveFirst(_interruptEvaluations.Count);
        }

        var choice = ChooseAmong(ref context, set, rankFloor: 0, trace, evaluated: null);
        SetPath(trace, choice.IsNone ? DecisionPath.Nothing : DecisionPath.FreshPick);
        return choice;
    }

    /// <summary>Carries out choice: calls its behavior's Act, with the commitment's target when it continues one, and starts a commitment when a fresh pick's grant asks for one.</summary>
    public BehaviorDecision Act(ref TContext context, BehaviorSet<TContext> set, in BehaviorChoice choice, in BehaviorCommitment commitment)
    {
        if (choice.IsNone)
        {
            return BehaviorDecision.None;
        }

        ref readonly var entry = ref set[choice.EntryIndex];
        if (choice.ContinuesCommitment)
        {
            context.CommitmentTarget = commitment.Target;
            entry.Behavior.Act(ref context, entry.Grant);
            return new BehaviorDecision(choice, StartedCommitment: false, commitment);
        }

        context.CommitmentTarget = EntityKey.None;
        entry.Behavior.Act(ref context, entry.Grant);
        if (entry.Grant.CommitFrames == 0)
        {
            return new BehaviorDecision(choice, StartedCommitment: false, default);
        }

        var started = new BehaviorCommitment(entry.SessionId, FrameDeadline.After(context.Now, entry.Grant.CommitFrames), context.CommitmentTarget);
        return new BehaviorDecision(choice, StartedCommitment: true, started);
    }

    /// <summary>Choose, then Act: for a caller deciding for one entity at a time.</summary>
    public BehaviorDecision Decide(ref TContext context, BehaviorSet<TContext> set, in BehaviorCommitment commitment, DecisionTrace? trace = null)
    {
        var choice = Choose(ref context, set, commitment, trace);
        return Act(ref context, set, choice, commitment);
    }

    /// <summary>The weighted pick among set's behaviors that apply above rankFloor.</summary>
    /// <param name="evaluated">The interrupt pass's buffer, filled with each entry's evaluation in set order; null for the fresh pick, which reads what the interrupt pass already evaluated from _interruptEvaluations instead of asking those behaviors again.</param>
    private BehaviorChoice ChooseAmong(ref TContext context, BehaviorSet<TContext> set, byte rankFloor, DecisionTrace? trace, List<BehaviorEvaluation>? evaluated)
    {
        _candidates.Clear();
        var traceStart = trace?.Count ?? 0;
        byte winnerRank = 0;
        for (var index = 0; index < set.Count; index++)
        {
            ref readonly var entry = ref set[index];
            if (entry.Rank <= rankFloor || entry.Rank < winnerRank)
            {
                break;
            }

            var evaluation = evaluated is null && index < _interruptEvaluations.Count ? _interruptEvaluations[index] : Evaluate(ref context, entry);
            evaluated?.Add(evaluation);
            if (evaluation.Rank <= rankFloor || evaluation.Rank < winnerRank || evaluation.Weight <= 0)
            {
                trace?.Add(new DecisionTraceEntry(index, entry.Behavior.Name, entry.Rank, evaluation.Rank, evaluation.Weight, entry.ResolvedWeight, 0f, 0f, 0f, IsCommitted: false, DecisionTraceOutcome.DidNotApply));
                continue;
            }

            var considerations = ConsiderationsProduct(ref context, entry.Grant);
            var runtime = considerations > 0 ? context.RuntimeWeightMultiplier(entry.Behavior.Tags) : 0f;
            var weight = evaluation.Weight * entry.ResolvedWeight * considerations * runtime;
            trace?.Add(new DecisionTraceEntry(index, entry.Behavior.Name, entry.Rank, evaluation.Rank, evaluation.Weight, entry.ResolvedWeight, considerations, runtime, weight,
                IsCommitted: false, weight > 0 ? DecisionTraceOutcome.NotDrawn : DecisionTraceOutcome.Vetoed));
            if (weight <= 0)
            {
                continue;
            }

            if (evaluation.Rank > winnerRank)
            {
                winnerRank = evaluation.Rank;
                _candidates.Clear();
            }

            _candidates.Add((index, evaluation.Rank, weight));
        }

        if (_candidates.Count == 0)
        {
            return BehaviorChoice.None;
        }

        var threshold = BestWeight() * options.CutoffFraction;
        RemoveBelow(threshold);
        var picked = _candidates.Count == 1 ? _candidates[0] : Draw(ref context);

        if (trace is not null)
        {
            MarkOutcomes(trace, traceStart, winnerRank, threshold, picked.EntryIndex);
        }

        return new BehaviorChoice((short)picked.EntryIndex, picked.Rank, ContinuesCommitment: false);
    }

    private (int EntryIndex, byte Rank, float Weight) Draw(ref TContext context)
    {
        var total = 0f;
        foreach (var candidate in _candidates)
        {
            total += candidate.Weight;
        }

        var roll = context.NextUnitRandom() * total;
        var cumulative = 0f;
        foreach (var candidate in _candidates)
        {
            cumulative += candidate.Weight;
            if (roll < cumulative)
            {
                return candidate;
            }
        }

        return _candidates[^1];
    }

    private void RemoveBelow(float threshold)
    {
        var kept = 0;
        for (var index = 0; index < _candidates.Count; index++)
        {
            if (_candidates[index].Weight >= threshold)
            {
                _candidates[kept++] = _candidates[index];
            }
        }

        _candidates.RemoveRange(kept, _candidates.Count - kept);
    }

    private float BestWeight()
    {
        var best = 0f;
        foreach (var candidate in _candidates)
        {
            best = System.Math.Max(best, candidate.Weight);
        }

        return best;
    }

    private static BehaviorEvaluation Evaluate(ref TContext context, in BehaviorSetEntry<TContext> entry)
    {
        var evaluation = entry.Behavior.Evaluate(ref context, entry.Grant);
        return evaluation.Rank > entry.Rank
            ? throw new InvalidOperationException($"Behavior '{entry.Behavior.Name}' evaluated at rank {evaluation.Rank}, above its grant's ceiling of {entry.Rank}.")
            : evaluation;
    }

    private static float ConsiderationsProduct(ref TContext context, BehaviorGrant<TContext> grant)
    {
        if (grant.Considerations is not { } considerations)
        {
            return 1f;
        }

        var product = 1f;
        for (var index = 0; index < considerations.Count && product > 0; index++)
        {
            product *= System.Math.Max(0f, considerations[index].Evaluate(ref context));
        }

        return product;
    }

    private static void MarkOutcomes(DecisionTrace trace, int traceStart, byte winnerRank, float threshold, int pickedEntryIndex)
    {
        for (var index = traceStart; index < trace.Count; index++)
        {
            var entry = trace[index];
            if (entry.Outcome != DecisionTraceOutcome.NotDrawn)
            {
                continue;
            }

            var outcome = entry.Rank < winnerRank ? DecisionTraceOutcome.Outranked
                : entry.FinalWeight < threshold ? DecisionTraceOutcome.BelowCutoff
                : entry.EntryIndex == pickedEntryIndex ? DecisionTraceOutcome.Chosen
                : DecisionTraceOutcome.NotDrawn;
            trace[index] = entry with { Outcome = outcome };
        }
    }

    private static void SetPath(DecisionTrace? trace, DecisionPath path)
    {
        if (trace is not null)
        {
            trace.Path = path;
        }
    }
}
