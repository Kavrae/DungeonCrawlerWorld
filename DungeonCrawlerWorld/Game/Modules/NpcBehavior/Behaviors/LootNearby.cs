using Engine.Decisions;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Loots a corpse it may loot, or an item pile it can take anything from, on or beside it (NpcLooting).</summary>
/// <remarks>
/// Evaluate finds the candidate and only reads; Act loots it, asking again whether it still may, since a neighbour that
/// acted first may have emptied or opened it -- and idles when it may not. When every corpse found was reserved for
/// someone else, the decision waits to retry corpses, whatever it chose (NpcDecisionFindings.RetryCorpsesLater).
/// </remarks>
public sealed class LootNearby() : NpcBehavior(BuiltInBehaviorIds.LootNearby, "Loot nearby", [])
{
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        if (!context.HasOtherOccupantNear())
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        var found = context.Services.Looting.TryFind(context.EntityId, context.Transform, context.Now, out var target, out var everyCorpseReserved);
        context.Findings.RetryCorpsesLater = everyCorpseReserved;
        if (!found)
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        context.Findings.LootTarget = target;
        return Applies(grant);
    }

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        if (context.TryLoot(context.Findings.LootTarget))
        {
            context.Step(null);
            return;
        }

        context.Idle();
    }
}
