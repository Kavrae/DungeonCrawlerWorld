using Engine.Decisions;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Tags;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Attacks the player from range when the player is hostile, not beside it, and in reach of a ranged attack it can use now.</summary>
/// <remarks>
/// TEMPORARY in one way: only ever at the player, until "Spatial queries for NPC decisions" gives it other targets. A
/// ranged attack is any action tagged Delivery.Ranged and Action.Attack, not one named by Guid, aimable at the player's
/// tile (TargetResolution.IsAimable: in range and, if it needs it, in sight). Act picks uniformly among those and aims
/// in Target or Ground mode on a coin flip. Adjacent counts across layers, so a Flying creature over the player doesn't
/// cast at point-blank range.
/// </remarks>
public sealed class RangedAttack() : NpcBehavior(BuiltInBehaviorIds.RangedAttack, "Ranged attack", [NpcDrives.Aggression])
{
    /// <remarks>Whether it holds a ranged attack at all is asked first: most creatures hold none, and that answer reads only its blueprint.</remarks>
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        var services = context.Services;
        services.CollectActions(context.EntityId, GameTags.DeliveryRanged, GameTags.ActionAttack, services.CandidateActions);
        return services.CandidateActions.Count > 0 && TryGetTarget(ref context, out var playerPosition) && KeepAimable(ref context, playerPosition) > 0
            ? Applies(grant)
            : BehaviorEvaluation.DoesNotApply;
    }

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        var services = context.Services;
        var candidates = services.CandidateActions;
        services.CollectActions(context.EntityId, GameTags.DeliveryRanged, GameTags.ActionAttack, candidates);
        if (!TryGetTarget(ref context, out var playerPosition) || KeepAimable(ref context, playerPosition) == 0)
        {
            return;
        }

        var action = candidates[context.Rolls.Next(0, candidates.Count)];
        var playerEntityId = services.Player.PlayerEntityId;
        var selection = context.Rolls.Next(0, 2) == 0
            ? services.Targeting.SelectEntity(context.EntityId, action.Activator, playerEntityId)
            : services.Targeting.Select(context.EntityId, action.Activator, TargetingMode.Ground, playerPosition, context.Now);
        context.QueueAction(action.Id, selection);
    }

    private static bool TryGetTarget(ref NpcDecisionContext context, out Vector3Int playerPosition)
    {
        var services = context.Services;
        var playerEntityId = services.Player.PlayerEntityId;
        playerPosition = default;
        if (playerEntityId < 0 || !context.IsHostile(playerEntityId) || !services.Transforms.TryGetReadonly(playerEntityId, out var playerTransform))
        {
            return false;
        }

        playerPosition = playerTransform.Position;
        return GridDistance.ChebyshevDistance(context.Transform.Position, playerPosition) > 1;
    }

    /// <summary>Keeps, of the ranged attacks already in the services' candidate actions, those it could aim at playerPosition and use now, and returns how many.</summary>
    private static int KeepAimable(ref NpcDecisionContext context, Vector3Int playerPosition)
    {
        var services = context.Services;
        var candidates = services.CandidateActions;
        var kept = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var action = candidates[index];
            if (services.Targeting.IsAimable(context.EntityId, action.Activator.Targeting, playerPosition) && services.IsUsable(context.EntityId, action, context.Now))
            {
                candidates[kept++] = action;
            }
        }

        candidates.RemoveRange(kept, candidates.Count - kept);
        return kept;
    }
}
