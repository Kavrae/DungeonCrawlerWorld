using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Utilities;

namespace Game.Modules.Core.Components;

/// <summary>The one place the shared action lock is read and written. Every gate (movement, Immediate/Delayed activation, consumables, inspection) goes through IsBlocked, and every lock through Lock.</summary>
/// <remarks>Deadline-based: locking writes the frame the entity is next free on, and nothing ticks it down (see ActionLockComponent). Both methods therefore need the current simulation frame -- a system passes EngineTime.FrameCount, anything else a SimulationClock.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ActionLockGate
{
    /// <summary>The lock duration most actions/items use -- the default an entity's own ActionLockComponent.StandardLockFrames is seeded with at construction.</summary>
    public static readonly ushort StandardLockFrames = (ushort)GameTiming.FramesForSeconds(1f);

    /// <summary>Whether entityId is currently barred from acting. An entity with no ActionLockComponent at all reads as blocked, unchanged from when this was a countdown.</summary>
    public static bool IsBlocked(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, long now) =>
        !actionLocks.TryGetReadonly(entityId, out var actionLock) || !FrameDeadline.IsReached(actionLock.UnlockedAtFrame, now);

    /// <summary>Locks entityId for framesToWait frames from now, or for the entity's own ActionLockComponent.StandardLockFrames if null. Sets the UI's total alongside the deadline, so a fresh action always resets the fraction's denominator too.</summary>
    public static void Lock(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, long now, ushort? framesToWait = null) =>
        actionLocks.TryUpdate(entityId, (Now: now, Frames: framesToWait), static (ref ActionLockComponent actionLock, (long Now, ushort? Frames) state) =>
        {
            var resolved = state.Frames ?? actionLock.StandardLockFrames;
            actionLock.CurrentLockTotalFrames = resolved;
            actionLock.UnlockedAtFrame = FrameDeadline.After(state.Now, resolved);
        });

    /// <summary>Frames left on the current lock, 0 once it has passed -- what the HUD fills divide by CurrentLockTotalFrames.</summary>
    public static int FramesRemaining(in ActionLockComponent actionLock, long now) => FrameDeadline.Remaining(actionLock.UnlockedAtFrame, now);
}
