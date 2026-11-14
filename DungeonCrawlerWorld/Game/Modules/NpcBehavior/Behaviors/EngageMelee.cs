using Engine.Decisions;
using Game.Modules.Actions;
using Game.Tags;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Attacks in melee when something hostile stands beside it, with any melee action it can use now.</summary>
/// <remarks>
/// A melee action is any action tagged Delivery.Melee (parent-aware: Quick Attack's Delivery.Melee.Unarmed counts), not
/// one named by Guid. Act picks uniformly among the ones it can use and swings at its whole Adjacent ring, which the
/// activation resolves into whoever stands there.
/// </remarks>
public sealed class EngageMelee() : NpcBehavior(BuiltInBehaviorIds.EngageMelee, "Engage in melee", [NpcDrives.Aggression])
{
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        if (!context.HasHostileAdjacent())
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        var services = context.Services;
        services.CollectActions(context.EntityId, GameTags.DeliveryMelee, default, services.CandidateActions);
        foreach (var action in services.CandidateActions)
        {
            if (services.IsUsable(context.EntityId, action, context.Now))
            {
                return Applies(grant);
            }
        }

        return BehaviorEvaluation.DoesNotApply;
    }

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        var services = context.Services;
        var candidates = services.CandidateActions;
        services.CollectActions(context.EntityId, GameTags.DeliveryMelee, default, candidates);
        services.KeepUsable(context.EntityId, candidates, context.Now);
        if (candidates.Count == 0)
        {
            return;
        }

        var action = candidates[context.Rolls.Next(0, candidates.Count)];
        context.QueueAction(action.Id, TargetSelection.Ground(context.Transform.Position, action.Activator.Targeting));
    }
}
