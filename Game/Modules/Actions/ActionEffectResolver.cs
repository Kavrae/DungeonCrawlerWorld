using Game.Effects;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.ProcessingTier;
using Game.Tags;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Per-activation orchestration shared by ActionActivationSystem (Immediate/FreeCast) and
/// DelayedActionSystem (a Delayed action's windup completing) -- publishes ActionActivatedEvent,
/// builds the source-fixed half of an EffectContext (ActivatorTags: action.Tags, for
/// DirectDamage's ability-score bonus), walks target tiles via IMapQuery.GetOccupantEntityIdsAt,
/// and calls EffectSequence.Apply(action.Effects, ...) once per resolved target -- once per
/// activation even for a multi-tile target the shape covers several cells of. Contains
/// no per-effect-kind knowledge at all -- what an action's effects actually do lives entirely on
/// the Effect/IEffectEntry types themselves. Takes the already-resolved action
/// (ActionInstanceQueries.TryResolveEffectiveAction, not a raw ActionCatalog lookup) so a
/// per-instance Override -- e.g. a flat damage number, see ActionInstanceComponent's own doc
/// comment -- is already baked into action.Effects by the time this runs.
///
/// dodgingEntities, when wired, gates a whole target skip -- not a per-effect-entry concern, so it
/// stays a resolver-level check rather than a new EffectContext field every IEffectEntry
/// would otherwise need to know about. See DodgingComponent's own doc comment.
///
/// A GameTags.TraitStaggering action publishes EntityStaggeredEvent for each target it hit other than its
/// source -- after the dodge skip, so a dodged hit never staggers.
/// </summary>
public static class ActionEffectResolver
{
    public static void Apply(
        ActionDefinition action,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        EffectServices effectServices,
        IMapQuery mapQuery,
        long now,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        ProcessingTierQuery processingTiers)
    {
        var eventBus = effectServices.EventBus;
        eventBus.Publish(new ActionActivatedEvent(sourceEntityId, action.Id));

        var context = EffectContext.FromEntity(effectServices, sourceEntityId, targetEntityId: sourceEntityId, action.Name, action.Tags, now);

        var isDodgeable = action.Tags.Has(GameTags.TraitDodgeable);
        var isStaggering = action.Tags.Has(GameTags.TraitStaggering);

        HashSet<int>? resolvedTargetIds = targetTiles.Count > 1 ? [] : null;

        foreach (var tile in targetTiles)
        {
            foreach (var targetEntityId in mapQuery.GetOccupantEntityIdsAt(tile))
            {
                if (!processingTiers.IsSimulated(targetEntityId))
                {
                    continue;
                }

                if (resolvedTargetIds is not null && !resolvedTargetIds.Add(targetEntityId))
                {
                    continue;
                }

                if (isDodgeable && dodgingEntities.Has(targetEntityId))
                {
                    effectServices.FloatingTextFeed.Publish(targetEntityId, FloatingTextKind.Dodged, 0);
                    continue;
                }

                EffectSequence.Apply(action.Effects, context with { TargetEntityId = targetEntityId });

                if (isStaggering && targetEntityId != sourceEntityId)
                {
                    eventBus.Publish(new EntityStaggeredEvent(targetEntityId, context.Source));
                }
            }
        }
    }
}
