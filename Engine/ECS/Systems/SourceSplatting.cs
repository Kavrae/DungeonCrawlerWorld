using Engine.ECS.Components.Stores;
using Engine.Math;

namespace Engine.ECS.Systems;

/// <summary>
/// Shared chain-walk-and-splat mechanics for reacting to a MultiComponentPool&lt;TSource&gt;
/// entity's own lifecycle (initial bulk scatter, or an old-position/new-position move) into a
/// derived per-cell accumulator -- the shape Game.Modules.StatusEffectAura.AuraGrid (via
/// StatusEffectAuraSystem) and Presentation.UI.MapTintGrid each independently re-implement
/// today, one accumulating an int stack total keyed by effect type, the other a weighted RGB
/// blend. Owns only the chain-walk-and-splat mechanics -- never the accumulation itself, since
/// an int sum and a weighted color sum have nothing in common numerically -- splat/unsplat stay
/// fully caller-defined delegates.
///
/// Deliberately doesn't touch event subscription or position lookup: the events involved
/// (AuraSourceAddedEvent/AuraSourceRemovedEvent) carry a Game-layer StatusEffectAuraSourceComponent,
/// and position lookup needs a Game-layer TransformComponent pool, so Engine can't reference
/// either directly (see DistanceFalloff's own doc comment on the same one-way-layering
/// constraint) -- this class only ever receives already-resolved (entityId, position) data via
/// caller-supplied delegates/values, the same way DistanceFalloff.ScatterManhattan takes a plain
/// visitor instead of reaching into Game state itself.
///
/// Also deliberately doesn't own any ProcessingTier-based deferral -- StatusEffectAuraSystem's
/// own tiered catch-up (see its ResyncSourceIfStale) is a gameplay-specific cost tradeoff
/// MapTintGrid, a purely cosmetic overlay, has no matching need for; each caller decides for
/// itself WHEN to call ResyncEntity, this type only guarantees it does the right thing once
/// called.
/// </summary>
public static class SourceSplatting
{
    /// <summary>Splats every currently-registered source once, skipping any entity tryGetPosition can't resolve a position for (e.g. not yet placed on the map).</summary>
    public static void ScatterAll<TSource>(MultiComponentPool<TSource> sources, Func<int, Vector3Int?> tryGetPosition, Action<int, TSource, Vector3Int> splat)
        where TSource : struct
    {
        var entityIds = sources.EntityIds;
        var components = sources.Components;
        for (var i = 0; i < entityIds.Length; i++)
        {
            if (tryGetPosition(entityIds[i]) is { } position)
            {
                splat(entityIds[i], components[i], position);
            }
        }
    }

    /// <summary>
    /// Chain-walks every one of entityId's own source instances, unsplatting each at
    /// oldPosition (skipped entirely when null -- a first-ever placement has nothing to
    /// unsplat) then splatting it at newPosition. The shared remove-old/add-new shape both a
    /// moved source and a first-time placement need, regardless of what "splat" actually
    /// accumulates.
    /// </summary>
    public static void ResyncEntity<TSource>(MultiComponentPool<TSource> sources, int entityId, Vector3Int? oldPosition, Vector3Int newPosition, Action<TSource, Vector3Int> unsplat, Action<TSource, Vector3Int> splat)
        where TSource : struct
    {
        for (var denseIndex = sources.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = sources.GetNextDenseIndex(denseIndex))
        {
            var source = sources.GetReadonlyByDenseIndex(denseIndex);
            if (oldPosition is { } old)
            {
                unsplat(source, old);
            }

            splat(source, newPosition);
        }
    }
}
