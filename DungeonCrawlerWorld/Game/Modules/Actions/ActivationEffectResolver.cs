using Game.Effects;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.ProcessingTier;
using Game.Tags;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>What a kind of use adds at each target it reaches, around the shared walk (ActivationEffectResolver): whether it lands there at all, and anything it does before or after its effects.</summary>
/// <remarks>Implemented by a struct, so the walk is specialized per rule and nothing is allocated per use. A potion's is one: it lands only on something with health, punishes cooldown abuse, then resets the cooldown.</remarks>
public interface IActivationTargetRule
{
    /// <summary>Whether the use lands on targetEntityId, once the seam and Dodge have let it through.</summary>
    bool Admits(int targetEntityId);

    /// <summary>Runs on a target it lands on, before its effects, with the context they apply in.</summary>
    void BeforeEffects(int targetEntityId, in EffectContext context);

    /// <summary>Runs on a target it lands on, after its effects.</summary>
    void AfterEffects(int targetEntityId);
}

/// <summary>The rule for a use with nothing of its own to add: it lands on everyone it reaches.</summary>
public readonly struct EveryTargetRule : IActivationTargetRule
{
    public bool Admits(int targetEntityId) => true;

    public void BeforeEffects(int targetEntityId, in EffectContext context)
    {
    }

    public void AfterEffects(int targetEntityId)
    {
    }
}

/// <summary>
/// Applies one use of an action or an item where its resolved targets are: the one walk both share, called when an
/// Immediate or FreeCast use activates and when a Delayed one's windup resolves. Publishes ActivationUsedEvent, builds
/// the source-fixed half of an EffectContext (ActivatorTags: the definition's tags, for DirectDamage's ability-score
/// bonus), walks the resolved targets (TargetResolution) -- or reaches only the marked entity for a MarkedOnly use --
/// and applies the definition's effects once per target reached, once per use even for a multi-tile target the shape
/// covers several cells of. Contains no per-effect-kind knowledge: what effects do lives on the IEffectEntry types,
/// and what a kind of use adds per target is its IActivationTargetRule.
///
/// A target is reached unless it is across the simulated/frozen seam, or dodging a use tagged GameTags.TraitDodgeable
/// (a whole-target skip, not a per-entry concern -- see DodgingComponent), or its rule doesn't admit it. Dead targets
/// are reached: a corpse can be acted on. A GameTags.TraitStaggering use publishes EntityStaggeredEvent for each
/// target it reached other than its source -- after the dodge skip, so a dodged hit never staggers.
///
/// The entries placed once per use follow the marked entity's fate: one the use doesn't reach receives none of them,
/// and only the AtLocation ones are placed. The OnEachTile entries land on every resolved tile whoever was reached:
/// they change the tiles, not who stands there.
///
/// A projectile's path hits (ResolvedTargets.PathHitEntityIds) are reached first, in the order it met them, and an
/// entity both on the way and in the shape is reached once.
/// </summary>
public static class ActivationEffectResolver
{
    private const int OccupantIdBufferLength = 16;

    /// <summary>Applies an action's use with no rule of its own.</summary>
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
        var everyTarget = default(EveryTargetRule);
        Apply(action, ActivatableKind.Action, sourceEntityId, targetTiles, resolved, effectServices, mapQuery, now, dodgingEntities, processingTiers, [], ref everyTarget);
    }

    /// <summary>Applies definition's use by sourceEntityId where resolved landed.</summary>
    /// <param name="reachedScratch">Cleared and used to reach each entity once; the caller's, so nothing is allocated per use.</param>
    /// <param name="durationScaleMultiplier">Scales every duration the effects carry; only a scroll sets it away from 1.</param>
    public static void Apply<TTargetRule>(
        ActivatableDefinition definition,
        ActivatableKind kind,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        ResolvedTargets resolved,
        EffectServices effectServices,
        IMapQuery mapQuery,
        long now,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        ProcessingTierQuery processingTiers,
        HashSet<int> reachedScratch,
        ref TTargetRule targetRule,
        float durationScaleMultiplier = 1.0f)
        where TTargetRule : struct, IActivationTargetRule
    {
        effectServices.EventBus.Publish(new ActivationUsedEvent(sourceEntityId, kind, definition.Id, definition.Tags));

        var walk = new Walk<TTargetRule>(
            definition,
            EffectContext.FromEntity(effectServices, sourceEntityId, targetEntityId: sourceEntityId, definition, now, durationScaleMultiplier),
            effectServices,
            processingTiers,
            dodgingEntities,
            definition.Tags.Has(GameTags.TraitDodgeable),
            definition.Tags.Has(GameTags.TraitStaggering));

        if (resolved.MarkedOnly)
        {
            var markedEntityReached = resolved.MarkedEntityId is not { } markedEntityId
                || walk.ApplyToTarget(markedEntityId, ref targetRule);

            ApplyOnce(definition, walk.Context, resolved, markedEntityReached);
            EffectSequence.ApplyOnEachTile(definition.Effects, walk.Context, targetTiles);
            return;
        }

        bool? markedOccupantReached = null;
        var pathHitCount = resolved.PathHitEntityIds.Count;
        reachedScratch.Clear();
        var tracksReached = targetTiles.Count > 1 || pathHitCount > 0;

        Span<int> occupantIdBuffer = stackalloc int[OccupantIdBufferLength];

        // Copied first: the list is TargetResolution's buffer, and applying an effect may resolve again.
        var pathHitEntityIds = pathHitCount <= occupantIdBuffer.Length ? occupantIdBuffer[..pathHitCount] : new int[pathHitCount];
        for (var index = 0; index < pathHitCount; index++)
        {
            pathHitEntityIds[index] = resolved.PathHitEntityIds[index];
        }

        foreach (var targetEntityId in pathHitEntityIds)
        {
            reachedScratch.Add(targetEntityId);
            var reached = walk.ApplyToTarget(targetEntityId, ref targetRule);
            if (targetEntityId == resolved.MarkedEntityId)
            {
                markedOccupantReached = reached;
            }
        }

        foreach (var tile in targetTiles)
        {
            // Copied first: applying an effect can change who occupies the tile.
            var occupantIds = mapQuery.GetOccupantEntityIdSpanAt(tile);
            var targetEntityIds = occupantIds.Length <= occupantIdBuffer.Length ? occupantIdBuffer[..occupantIds.Length] : new int[occupantIds.Length];
            occupantIds.CopyTo(targetEntityIds);

            foreach (var targetEntityId in targetEntityIds)
            {
                if (tracksReached && !reachedScratch.Add(targetEntityId))
                {
                    continue;
                }

                var reached = walk.ApplyToTarget(targetEntityId, ref targetRule);
                if (targetEntityId == resolved.MarkedEntityId)
                {
                    markedOccupantReached = reached;
                }
            }
        }

        var markedEntityWasReached = markedOccupantReached
            ?? (resolved.MarkedEntityId is not { } markedOutsideArea || walk.Reaches(markedOutsideArea, ref targetRule));

        ApplyOnce(definition, walk.Context, resolved, markedEntityWasReached);
        EffectSequence.ApplyOnEachTile(definition.Effects, walk.Context, targetTiles);
    }

    /// <summary>Applies the entries placed once per use: all of them when the marked entity was reached (or nothing was marked), only the AtLocation ones when it was missed.</summary>
    private static void ApplyOnce(ActivatableDefinition definition, EffectContext context, ResolvedTargets resolved, bool markedEntityReached)
    {
        if (markedEntityReached)
        {
            EffectSequence.ApplyOnce(definition.Effects, context, resolved.MarkedEntityId, resolved.Centre);
        }
        else
        {
            EffectSequence.ApplyAtLocation(definition.Effects, context, resolved.Centre);
        }
    }

    /// <summary>What one use's walk holds the same for every target.</summary>
    private readonly struct Walk<TTargetRule>(
        ActivatableDefinition definition,
        EffectContext context,
        EffectServices effectServices,
        ProcessingTierQuery processingTiers,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        bool isDodgeable,
        bool isStaggering)
        where TTargetRule : struct, IActivationTargetRule
    {
        public EffectContext Context { get; } = context;

        /// <summary>Applies the use to one target it reaches (Reaches), and a Staggering hit staggers anyone but its source.</summary>
        /// <returns>Whether the use reached the target.</returns>
        public bool ApplyToTarget(int targetEntityId, ref TTargetRule targetRule)
        {
            if (!Reaches(targetEntityId, ref targetRule))
            {
                return false;
            }

            var targetContext = Context with { TargetEntityId = targetEntityId };
            targetRule.BeforeEffects(targetEntityId, in targetContext);
            EffectSequence.ApplyOnEachTarget(definition.Effects, targetContext);

            if (isStaggering && targetEntityId != Context.SourceEntityId)
            {
                effectServices.EventBus.Publish(new EntityStaggeredEvent(targetEntityId, Context.Source));
            }

            targetRule.AfterEffects(targetEntityId);
            return true;
        }

        /// <summary>Whether the use reaches targetEntityId: never across the simulated/frozen seam, never a dodging target of a Dodgeable use (which shows "Dodged"), and only one its rule admits.</summary>
        public bool Reaches(int targetEntityId, ref TTargetRule targetRule)
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

            return targetRule.Admits(targetEntityId);
        }
    }
}
