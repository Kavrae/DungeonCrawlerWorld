using Engine.ECS.Components.Stores;

namespace Game.Modules.Core.Components;

public static class ActionLockGate
{
    public static bool IsBlocked(PackedComponentPool<ActionLockComponent> actionLocks, int entityId) =>
        !actionLocks.TryGetReadonly(entityId, out var actionLock) || actionLock.LockFramesRemaining > 0;

    public static void Lock(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, short framesToWait) =>
        actionLocks.TryUpdate(entityId, framesToWait, static (ref ActionLockComponent actionLock, short frames) =>
        {
            actionLock.TotalLockFrames = frames;
            actionLock.LockFramesRemaining = frames;
        });
}
