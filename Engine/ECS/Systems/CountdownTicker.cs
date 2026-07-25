using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary>
/// Shared "decrement a per-entity countdown once per real frame; once it reaches 0, tick"
/// loop -- the exact shape BurningSystem, PoisonSystem, ContactDamageSystem, and
/// StatusEffectAuraSystem all independently re-implemented before this existed. Deliberately
/// does NOT own entity-id source selection (an EntityStripeSet bucket or a pool's own
/// EntityIds are both a plain ReadOnlySpan&lt;int&gt;, and StripeCount=1 makes them
/// interchangeable -- see those systems' own doc comments for why StripeCount=1 matters here)
/// or what "ticking" actually does (fully game- and effect-specific) -- only the
/// decrement-or-fire mechanics and the safety rule every caller needs regardless: an entity
/// whose T should be removed can't be removed mid-scan (PackedComponentPool.Remove swaps the
/// last entry into the removed slot, which would corrupt whichever bucket/span is currently
/// being enumerated), so removal is deferred until the whole scan completes.
/// </summary>
public static class CountdownTicker
{
    /// <summary>
    /// For each entityId in entityIds with a T in pool: decrements FramesUntilNextTick while
    /// it's still &gt; 1, otherwise calls onTick(entityId, component). onTick returning true
    /// means "remove this entity's T entirely" (collected into pendingRemovals and applied
    /// only after the full scan below completes); false means onTick already reset
    /// FramesUntilNextTick (and whatever else it owns) itself, so the component stays as-is.
    /// pendingRemovals is caller-owned and reused across calls (cleared here) purely to avoid
    /// a fresh List allocation every frame -- pass a field, not a local.
    /// </summary>
    public static void Tick<T>(
        PackedComponentPool<T> pool,
        ReadOnlySpan<int> entityIds,
        List<int> pendingRemovals,
        Func<int, T, bool> onTick)
        where T : struct, ITickCountdown
    {
        pendingRemovals.Clear();

        foreach (var entityId in entityIds)
        {
            if (!pool.TryGetReadonly(entityId, out var component))
            {
                continue;
            }

            if (component.FramesUntilNextTick > 1)
            {
                pool.TryUpdate(entityId, static (ref T c) => c.FramesUntilNextTick--);
                continue;
            }

            if (onTick(entityId, component))
            {
                pendingRemovals.Add(entityId);
            }
        }

        foreach (var entityId in pendingRemovals)
        {
            pool.Remove(entityId);
        }
    }
}
