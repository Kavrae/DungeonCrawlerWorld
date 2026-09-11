using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary> Shared "decrement a per-entity countdown once per real frame; once it reaches 0, tick" loop</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class CountdownTicker
{
    /// <summary>Ticks all entities in the given span with the specified pool.</summary>
    /// <remarks>Once an entity's countdown reaches 0, its <see cref="ITickCountdown"/> component is updated via its custom updater</remarks>
    /// <typeparam name="T">The type of the countdown component.</typeparam>
    /// <param name="pool">The component pool containing the entities to tick.</param>
    /// <param name="entityIds">The IDs of the entities to tick.</param>
    /// <param name="pendingRemovals">A list to collect entities that need to be removed.</param>
    /// <param name="onTick">A function to call when an entity's countdown reaches 0.</param>
    /// <param name="framesPerVisit">The number of frames to decrement the countdown by, to account for entity striping.</param>
    public static void Tick<T>(
        PackedComponentPool<T> pool,
        ReadOnlySpan<int> entityIds,
        List<int> pendingRemovals,
        Func<int, T, bool> onTick,
        uint framesPerVisit = 1)
        where T : struct, ITickCountdown
    {
        pendingRemovals.Clear();

        foreach (var entityId in entityIds)
        {
            var remainingFrames = framesPerVisit;

            // Loops rather than firing once, because framesPerVisit can span several of this
            // countdown's own periods: a 60-frame burning tick on an entity visited every 960
            // frames (base StripeCount 15 at the Beyond tier's divisor) owes 16 ticks, not one.
            // Firing once regardless -- the previous behaviour -- under-applied every
            // damage/heal-over-time effect by roughly the entity's tier divisor, so how much
            // total damage a burning entity took depended on how far it happened to be standing
            // from the player. onTick's signature is deliberately unchanged: each call still
            // means exactly one period, so no consumer has to learn about catch-up.
            while (true)
            {
                // Re-read every iteration: onTick can remove the component outright, and it is
                // what re-arms FramesUntilNextTick for the next period.
                if (!pool.TryGetReadonly(entityId, out var component))
                {
                    break;
                }

                if ((uint)component.FramesUntilNextTick > remainingFrames)
                {
                    pool.TryUpdate(entityId, remainingFrames, static (ref T c, uint frames) => c.FramesUntilNextTick -= (ushort)frames);
                    break;
                }

                remainingFrames -= component.FramesUntilNextTick;

                if (onTick(entityId, component))
                {
                    pendingRemovals.Add(entityId);
                    break;
                }

                // A consumer whose onTick returns false without re-arming would otherwise spin
                // here forever, since remainingFrames would stop decreasing. Breaking out treats
                // it as "nothing further is owed this visit" rather than hanging the frame.
                if (!pool.TryGetReadonly(entityId, out var rearmed) || rearmed.FramesUntilNextTick == 0)
                {
                    break;
                }
            }
        }

        foreach (var entityId in pendingRemovals)
        {
            pool.Remove(entityId);
        }
    }
}
