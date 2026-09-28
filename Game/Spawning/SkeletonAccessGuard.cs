using Engine.ECS.Components;
using Engine.ECS.Systems;

namespace Game.Spawning;

/// <summary>Debug-only: throws when the simulation reads or writes a pool a skeleton doesn't hold yet, for a skeleton.</summary>
/// <remarks>
/// A skeleton's missing components read as absent, so a missed build doesn't fail on its own -- it
/// silently sees "no loot", "no health". Any gameplay touch of a skeleton must build it first
/// (CreatureSkeletons.EnsureBuilt); this makes forgetting that fail loudly in tests and Debug play.
/// Only while SystemManager is updating: presentation reads a skeleton freely between frames, and
/// never builds it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SkeletonAccessGuard(CreatureSkeletons skeletons, SystemManager systemManager) : IEntityAccessGuard
{
    public void OnAccess(Type componentType, int entityId)
    {
        if (systemManager.IsUpdating && skeletons.IsSkeleton(entityId))
        {
            throw new InvalidOperationException(
                $"The simulation touched {componentType.Name} of entity {entityId}, which is an unbuilt creature skeleton. " +
                "Whatever reached it must build it first (CreatureSkeletons.EnsureBuilt), or not reach unsimulated entities at all.");
        }
    }

    /// <summary>Sets a guard on every registered pool a skeleton doesn't hold (see EntityFactory.SkeletonComponentTypes).</summary>
    public static void Install(ComponentManager componentManager, SkeletonAccessGuard guard)
    {
        foreach (var pool in componentManager.AllPools)
        {
            if (!EntityFactory.SkeletonComponentTypes.Contains(pool.ComponentType))
            {
                pool.AccessGuard = guard;
            }
        }
    }
}
