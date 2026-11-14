using Engine.Decisions;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Modules.Movement;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Fight or flight: with a hostile beside it, runs from the nearest hostile it can see -- a step to the tile beside it farthest from that hostile.</summary>
/// <remarks>
/// It starts only with a hostile beside it, as the choice against EngageMelee: a hostile merely in sight is no reason to
/// run, and with nothing else at its rank Flee would always win. Weight (Parameter + missing health fraction) x the
/// hostiles in its sight within NpcDecisionContext.HostileSightRadius:
/// Parameter is a baseline panic, so an unhurt creature may still run. Its commitment's target is the hostile it ran
/// from, so while committed it keeps running from that one, even when another comes nearer; it ends early when that
/// hostile is gone, dead or no longer hostile, or no hostile is in sight. A creature that can't move, or has no step that
/// takes it farther away (cornered), doesn't flee -- it fights. The step is chosen without a roll: the farthest, the
/// first of equals.
/// </remarks>
public sealed class Flee() : NpcBehavior(BuiltInBehaviorIds.Flee, "Flee", [NpcDrives.Caution])
{
    private static readonly Vector3Int[] StepOffsets =
    [
        new(0, -1, 0), new(1, 0, 0), new(0, 1, 0), new(-1, 0, 0),
        new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0), new(-1, -1, 0),
    ];

    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        if (!context.CanMove || (context.CommitmentTarget.IsNone && !context.HasHostileAdjacent()))
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        var hostiles = context.HostilesInSight();
        if (hostiles == 0)
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        var fleeFrom = context.CommitmentTarget.IsNone ? context.NearestHostileInSight() : context.CommitmentTarget;
        if (!TryFindStepAway(ref context, fleeFrom, out var step))
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        context.Findings.FleeFrom = fleeFrom;
        context.Findings.FleeStep = step;
        var weight = (grant.Parameter + (1f - context.HealthFraction())) * hostiles;
        return weight > 0f ? new BehaviorEvaluation(grant.Rank, weight) : BehaviorEvaluation.DoesNotApply;
    }

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        context.CommitmentTarget = context.Findings.FleeFrom;
        context.Step(context.Findings.FleeStep);
    }

    /// <summary>The step beside it farthest from fleeFrom, when one takes it farther than it stands; false when fleeFrom is gone, dead or not hostile, or it is cornered.</summary>
    private static bool TryFindStepAway(ref NpcDecisionContext context, EntityKey fleeFrom, out Vector3Int step)
    {
        step = default;
        var services = context.Services;
        if (!services.EntityKeys.TryGetEntityId(fleeFrom, out var hostileEntityId) ||
            !context.IsHostile(hostileEntityId) ||
            !services.Transforms.TryGetReadonly(hostileEntityId, out var hostileTransform))
        {
            return false;
        }

        var position = context.Transform.Position;
        var size = context.Transform.Size;
        var isBlocking = services.Map.IsBlocking(context.EntityId);
        var bestDistance = SquaredDistanceBetweenFootprints(position, size, hostileTransform.Position, hostileTransform.Size);
        var found = false;
        foreach (var offset in StepOffsets)
        {
            var candidate = new Vector3Int(position.X + offset.X, position.Y + offset.Y, position.Z);
            var distance = SquaredDistanceBetweenFootprints(candidate, size, hostileTransform.Position, hostileTransform.Size);
            if (distance > bestDistance && MovementCandidates.CanStep(services.Map, position, candidate, size, context.EntityId, isBlocking))
            {
                bestDistance = distance;
                step = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The squared distance between the nearest tiles of two footprints: 0 when they overlap, 1 when they touch along a side.</summary>
    private static int SquaredDistanceBetweenFootprints(Vector3Int first, Vector2Byte firstSize, Vector3Int second, Vector2Byte secondSize)
    {
        var x = System.Math.Max(0, System.Math.Max(second.X - (first.X + firstSize.X - 1), first.X - (second.X + secondSize.X - 1)));
        var y = System.Math.Max(0, System.Math.Max(second.Y - (first.Y + firstSize.Y - 1), first.Y - (second.Y + secondSize.Y - 1)));
        return x * x + y * y;
    }
}
