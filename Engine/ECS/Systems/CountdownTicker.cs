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
            if (!pool.TryGetReadonly(entityId, out var component))
            {
                continue;
            }

            if ((uint)component.FramesUntilNextTick > framesPerVisit)
            {
                pool.TryUpdate(entityId, framesPerVisit, static (ref T c, uint frames) => c.FramesUntilNextTick -= (ushort)frames);
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
