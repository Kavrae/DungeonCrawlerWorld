using Engine.ECS.Components;
using Engine.ECS.Components.Stores;

namespace Engine.ECS.Systems;

/// <summary>CountdownTicker's shape, adapted for a MultiComponentPool</summary>
/// <remarks>
/// An entity may carry several simultaneous instances of T here (e.g. one per StatusEffectType
/// currently in range), each with its own independent countdown -- CountdownTicker itself can't
/// be reused directly since it's PackedComponentPool-only (one T per entity).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class MultiCountdownTicker
{
    /// <summary>Updates the countdown for all components in the pool.</summary>
    /// <remarks>Pending removals are returned to the caller so they can be processed in bulk after the full stack of updates has completed.</remarks>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <param name="pool">The component pool to update.</param>
    /// <param name="entityIds">The IDs of the entities to update based on the current entity stripe.</param>
    /// <param name="pendingRemovals">The list of components to remove as their countdown has reached 0.</param>
    /// <param name="onTick">The function to call for each component when it ticks down.</param>
    /// <param name="framesPerVisit">The number of frames between visits to each component.</param>
    public static void Tick<T>(
        MultiComponentPool<T> pool,
        ReadOnlySpan<int> entityIds,
        List<(int EntityId, T Component)> pendingRemovals,
        Func<int, T, bool> onTick,
        uint framesPerVisit = 1)
        where T : struct, ITickCountdown
    {
        pendingRemovals.Clear();

        foreach (var entityId in entityIds)
        {
            for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
            {
                var component = pool.GetReadonlyByDenseIndex(denseIndex);

                if ((uint)component.FramesUntilNextTick > framesPerVisit)
                {
                    pool.UpdateByDenseIndex(denseIndex, framesPerVisit, static (ref T c, uint frames) => c.FramesUntilNextTick -= (ushort)frames);
                    continue;
                }

                if (onTick(entityId, component))
                {
                    pendingRemovals.Add((entityId, component));
                }
            }
        }

        foreach (var (entityId, component) in pendingRemovals)
        {
            pool.RemoveFirst(entityId, component, static (ref readonly T candidate, T target) => EqualityComparer<T>.Default.Equals(candidate, target));
        }
    }
}
