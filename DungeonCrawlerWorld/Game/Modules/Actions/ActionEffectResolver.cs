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
/// DirectDamage's ability-score bonus), walks the resolved target tiles (TargetResolution) via IMapQuery.GetOccupantEntityIdSpanAt -- or reaches only the marked entity for a MarkedOnly activation --
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
///
/// The entries placed once per activation follow the marked entity's fate: one the action doesn't
/// reach (dodging, or not simulated) receives none of them, and only the AtLocation ones are placed.
/// </summary>
public static class ActionEffectResolver
{
    private const int OccupantIdBufferLength = 16;

    public static void Apply(
        ActionDefinition action,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        ResolvedTargets resolved,
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

        if (resolved.MarkedOnly)
        {
            var markedEntityReached = resolved.MarkedEntityId is not { } markedEntityId
                || ApplyToTarget(action, context, markedEntityId, effectServices, processingTiers, dodgingEntities, isDodgeable, isStaggering);

            ApplyOnce(action, context, resolved, markedEntityReached);
            return;
        }

        bool? markedOccupantReached = null;
        HashSet<int>? resolvedTargetIds = targetTiles.Count > 1 ? [] : null;

        Span<int> occupantIdBuffer = stackalloc int[OccupantIdBufferLength];

        foreach (var tile in targetTiles)
        {
            // Copied first: applying an effect can change who occupies the tile.
            var occupantIds = mapQuery.GetOccupantEntityIdSpanAt(tile);
            var targetEntityIds = occupantIds.Length <= occupantIdBuffer.Length ? occupantIdBuffer[..occupantIds.Length] : new int[occupantIds.Length];
            occupantIds.CopyTo(targetEntityIds);

            foreach (var targetEntityId in targetEntityIds)
            {
                if (resolvedTargetIds is not null && !resolvedTargetIds.Add(targetEntityId))
                {
                    continue;
                }

                var reached = ApplyToTarget(action, context, targetEntityId, effectServices, processingTiers, dodgingEntities, isDodgeable, isStaggering);
                if (targetEntityId == resolved.MarkedEntityId)
                {
                    markedOccupantReached = reached;
                }
            }
        }

        var markedEntityWasReached = markedOccupantReached
            ?? (resolved.MarkedEntityId is not { } markedOutsideArea || Reaches(markedOutsideArea, effectServices, processingTiers, dodgingEntities, isDodgeable));

        ApplyOnce(action, context, resolved, markedEntityWasReached);
    }

    /// <summary>Applies the entries placed once per activation: all of them when the marked entity was reached (or nothing was marked), only the AtLocation ones when it was missed.</summary>
    private static void ApplyOnce(ActionDefinition action, EffectContext context, ResolvedTargets resolved, bool markedEntityReached)
    {
        if (markedEntityReached)
        {
            EffectSequence.ApplyOnce(action.Effects, context, resolved.MarkedEntityId, resolved.Centre);
        }
        else
        {
            EffectSequence.ApplyAtLocation(action.Effects, context, resolved.Centre);
        }
    }

    /// <summary>Applies the action to one target it reaches (Reaches), and a Staggering hit staggers anyone but its source.</summary>
    /// <returns>Whether the action reached the target.</returns>
    private static bool ApplyToTarget(ActionDefinition action, EffectContext context, int targetEntityId, EffectServices effectServices, ProcessingTierQuery processingTiers, PackedComponentPool<DodgingComponent> dodgingEntities, bool isDodgeable, bool isStaggering)
    {
        if (!Reaches(targetEntityId, effectServices, processingTiers, dodgingEntities, isDodgeable))
        {
            return false;
        }

        EffectSequence.ApplyOnEachTarget(action.Effects, context with { TargetEntityId = targetEntityId });

        if (isStaggering && targetEntityId != context.SourceEntityId)
        {
            effectServices.EventBus.Publish(new EntityStaggeredEvent(targetEntityId, context.Source));
        }

        return true;
    }

    /// <summary>Whether the action reaches targetEntityId: never across the simulated/frozen seam, and never a dodging target of a Dodgeable action, which shows "Dodged".</summary>
    private static bool Reaches(int targetEntityId, EffectServices effectServices, ProcessingTierQuery processingTiers, PackedComponentPool<DodgingComponent> dodgingEntities, bool isDodgeable)
    {
        if (!processingTiers.IsSimulated(targetEntityId))
        {
            return false;
        }

        if (isDodgeable && dodgingEntities.Has(targetEntityId))
        {
            effectServices.FloatingTextFeed.Publish(targetEntityId, FloatingTextKind.Dodged, 0);
            return false;
        }

        return true;
    }
}
