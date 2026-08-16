using Engine.ECS.Components.Stores;
using Engine.Utilities;

namespace Game.Modules.Core.Components;

/// <summary>Provides methods for managing action locks.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ActionLockGate
{
    /// <summary>The lock duration most actions/items use -- the default an entity's own ActionLockComponent.StandardLockFrames is seeded with at construction.</summary>
    public static readonly ushort StandardLockFrames = (ushort)GameTiming.FramesForSeconds(1f);

    /// <summary>Determines whether the specified entity is currently locked.</summary>
    /// <param name="actionLocks"></param>
    /// <param name="entityId"></param>
    /// <returns></returns>
    public static bool IsBlocked(PackedComponentPool<ActionLockComponent> actionLocks, int entityId) =>
        !actionLocks.TryGetReadonly(entityId, out var actionLock) || actionLock.CurrentLockFramesRemaining > 0;

    /// <summary>Locks entityId for framesToWait real frames, or the entity's own ActionLockComponent.StandardLockFrames if null.</summary>
    /// <param name="actionLocks"></param>
    /// <param name="entityId"></param>
    /// <param name="framesToWait"></param>
    public static void Lock(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, ushort? framesToWait = null) =>
        actionLocks.TryUpdate(entityId, framesToWait, static (ref ActionLockComponent actionLock, ushort? frames) =>
        {
            var resolved = frames ?? actionLock.StandardLockFrames;
            actionLock.CurrentLockTotalFrames = resolved;
            actionLock.CurrentLockFramesRemaining = resolved;
        });
}
