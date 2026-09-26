using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;

namespace Game.Modules.Actions;

/// <summary>The one way a Delayed action's windup is cancelled before it resolves.</summary>
/// <remarks>
/// Removing PendingDelayedActionComponent is the whole cancel: DelayedActionSystem's timer wheel drops the entry as
/// stale when its frame comes round. The windup set the shared ActionLock when it began, so a caller chooses whether
/// the entity gets that time back (releaseLock) or loses it.
/// </remarks>
public static class WindupCancel
{
    /// <summary>Cancels <paramref name="entityId"/>'s windup, if one is in progress. Returns whether one was.</summary>
    public static bool TryCancel(
        PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
        PackedComponentPool<ActionLockComponent> actionLocks,
        int entityId,
        long now,
        bool releaseLock)
    {
        if (!pendingDelayedActions.Remove(entityId))
        {
            return false;
        }

        if (releaseLock)
        {
            ActionLockGate.Release(actionLocks, entityId, now);
        }

        return true;
    }
}
