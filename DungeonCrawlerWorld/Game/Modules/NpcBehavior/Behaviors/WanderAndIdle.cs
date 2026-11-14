using Engine.Decisions;
using Game.Modules.Movement;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Steps to a random free tile beside it; idles when none is free. Never for a creature that can't move.</summary>
public sealed class WanderStep() : NpcBehavior(BuiltInBehaviorIds.WanderStep, "Wander", [])
{
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant) =>
        context.CanMove ? Applies(grant) : BehaviorEvaluation.DoesNotApply;

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        var services = context.Services;
        var transform = context.Transform;
        if (MovementCandidates.TryPickRandomAdjacentPosition(services.Map, context.Rolls, context.EntityId, transform.Position, transform.Size, services.Map.IsBlocking(context.EntityId), out var candidatePosition))
        {
            context.Step(candidatePosition);
            return;
        }

        context.Idle();
    }
}

/// <summary>Waits, deciding nothing for a while (MovementCandidates.FramesToWaitIfNoOptions).</summary>
public sealed class Idle() : NpcBehavior(BuiltInBehaviorIds.Idle, "Idle", [])
{
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant) => Applies(grant);

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant) => context.Idle();
}
