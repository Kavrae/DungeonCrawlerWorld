using System.Globalization;
using System.Text;
using Engine.Decisions;
using Game;
using Game.Views;

namespace Presentation.UI.Content;

/// <summary>The Admin Mode inspection readout of an NPC's decisions: what holds it now, what it last chose and why, and what it decides with.</summary>
/// <remarks>
/// Built only when its key changes (a new decision, a new gate, a commitment starting or ending, the next tenth of a second), never per frame:
/// InspectionWindowContent keeps it in a KeyedCache. Lines end in '\n' alone, never Environment.NewLine: the font draws a
/// carriage return as a missing glyph.
/// </remarks>
public static class NpcDecisionText
{
    private const char LineBreak = '\n';

    /// <summary>Everything the readout shows that can change: rebuild when any of it does.</summary>
    /// <param name="NowInTenthsOfASecond">The clock at the precision the readout's times are shown at.</param>
    public readonly record struct Key(int EntityId, long LastDecisionFrame, NpcDecisionGate Gate, uint CommittedUntilFrame, long NowInTenthsOfASecond);

    public static Key KeyOf(NpcDecisionView view, int entityId) =>
        new(entityId, view.LastDecisionFrame(entityId), view.GateOf(entityId), view.TryGetCommitment(entityId, out var commitment, out _) ? commitment.CommittedUntilFrame : 0, view.Now * 10 / GameFrameRate.FramesPerSecond);

    public static string Build(Key key, NpcDecisionView view)
    {
        var text = new StringBuilder();
        var entityId = key.EntityId;
        var now = view.Now;

        text.Append("Now: ").Append(GateText(key.Gate, now)).Append(LineBreak);

        if (key.LastDecisionFrame >= 0)
        {
            var trace = view.LastTrace;
            text.Append("Last decision: ").Append(Seconds(now - key.LastDecisionFrame)).Append(" ago -- ")
                .Append(view.LastChoice(entityId) ?? "nothing applied").Append(" (").Append(PathText(trace.Path)).Append(')').Append(LineBreak);
        }
        else
        {
            text.Append("Last decision: none yet since inspected").Append(LineBreak);
        }

        if (view.TryGetCommitment(entityId, out var commitment, out var committedBehavior))
        {
            text.Append("Committed: ").Append(committedBehavior).Append(" for ").Append(Seconds(commitment.CommittedUntilFrame - now));
            text.Append(commitment.Target.IsNone ? string.Empty : $" to {commitment.Target}").Append(LineBreak);
        }

        text.Append(LineBreak).Append("Behaviors (rank, weight, parameter):").Append(LineBreak);
        foreach (var entry in view.SetOf(entityId).Entries)
        {
            text.Append("  ").Append(entry.Rank).Append("  ").Append(entry.Behavior.Name)
                .Append("  x").Append(Number(entry.ResolvedWeight));
            if (entry.Grant.Parameter != 0f)
            {
                text.Append("  p ").Append(Number(entry.Grant.Parameter));
            }

            text.Append(LineBreak);
        }

        if (key.LastDecisionFrame >= 0)
        {
            text.Append(LineBreak).Append("Last decision, layer by layer:").Append(LineBreak);
            foreach (var entry in view.LastTrace.Entries)
            {
                text.Append("  ").Append(entry.BehaviorName).Append("  rank ").Append(entry.Rank).Append('/').Append(entry.RankCeiling);
                if (entry.Outcome != DecisionTraceOutcome.DidNotApply)
                {
                    text.Append("  ").Append(Number(entry.IntrinsicWeight))
                        .Append(" x ").Append(Number(entry.ResolvedWeight))
                        .Append(" x ").Append(Number(entry.ConsiderationsProduct))
                        .Append(" x ").Append(Number(entry.RuntimeMultiplier))
                        .Append(" = ").Append(Number(entry.FinalWeight));
                }

                text.Append("  ").Append(OutcomeText(entry.Outcome, entry.IsCommitted)).Append(LineBreak);
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string GateText(NpcDecisionGate gate, long now) => gate.Kind switch
    {
        NpcDecisionGateKind.Free => "free to decide",
        NpcDecisionGateKind.Player => "the player: nothing decides for it",
        NpcDecisionGateKind.Dead => "dead",
        NpcDecisionGateKind.Idling => $"idling, {Seconds(gate.UntilFrame - now)} left",
        NpcDecisionGateKind.Locked => $"action locked, {Seconds(gate.UntilFrame - now)} left",
        NpcDecisionGateKind.Stepping => "taking a step",
        _ => gate.Kind.ToString(),
    };

    private static string PathText(DecisionPath path) => path switch
    {
        DecisionPath.FreshPick => "fresh pick",
        DecisionPath.Interrupt => "interrupted a commitment",
        DecisionPath.Continued => "kept its commitment",
        _ => "nothing applied",
    };

    private static string OutcomeText(DecisionTraceOutcome outcome, bool isCommitted) => outcome switch
    {
        DecisionTraceOutcome.Chosen => isCommitted ? "kept" : "chosen",
        DecisionTraceOutcome.DidNotApply => "did not apply",
        DecisionTraceOutcome.Vetoed => "vetoed",
        DecisionTraceOutcome.Outranked => "outranked",
        DecisionTraceOutcome.BelowCutoff => "under the cutoff",
        DecisionTraceOutcome.NotDrawn => "not drawn",
        _ => outcome.ToString(),
    };

    private static string Seconds(long frames) =>
        (System.Math.Max(0, frames) / (float)GameFrameRate.FramesPerSecond).ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
