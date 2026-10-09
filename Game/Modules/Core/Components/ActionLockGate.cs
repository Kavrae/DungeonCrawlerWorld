using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;

namespace Game.Modules.Core.Components;

/// <summary>The one place the shared action lock is read and written. Every gate (movement, Immediate/Delayed activation, consumables, inspection) goes through IsBlocked, and every lock through Lock.</summary>
/// <remarks>Deadline-based: locking writes the frame the entity is next free on, and nothing ticks it down (see ActionLockComponent). Both methods therefore need the current simulation frame -- a system passes EngineTime.FrameCount, anything else a SimulationClock.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ActionLockGate
{
    /// <summary>Whether entityId is currently barred from acting. An entity with no ActionLockComponent at all reads as blocked, unchanged from when this was a countdown.</summary>
    public static bool IsBlocked(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, long now) =>
        !actionLocks.TryGetReadonly(entityId, out var actionLock) || !FrameDeadline.IsReached(actionLock.UnlockedAtFrame, now);

    /// <summary>Locks entityId for framesToWait frames from now. Sets the UI's total alongside the deadline, so a fresh action always resets the fraction's denominator too.</summary>
    /// <remarks>A lock with no duration of its own is the entity's standard lock -- the caller resolves it (StandardActionLockFrames).</remarks>
    public static void Lock(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, long now, ushort framesToWait) =>
        actionLocks.TryUpdate(entityId, (Now: now, Frames: framesToWait), static (ref ActionLockComponent actionLock, (long Now, ushort Frames) state) =>
        {
            actionLock.CurrentLockTotalFrames = state.Frames;
            actionLock.UnlockedAtFrame = FrameDeadline.After(state.Now, state.Frames);
        });

    /// <summary>Frees entityId from its current lock as of now.</summary>
    public static void Release(PackedComponentPool<ActionLockComponent> actionLocks, int entityId, long now) =>
        Lock(actionLocks, entityId, now, framesToWait: 0);

    /// <summary>Frames left on the current lock, 0 once it has passed -- what the HUD fills divide by CurrentLockTotalFrames.</summary>
    public static int FramesRemaining(in ActionLockComponent actionLock, long now) => FrameDeadline.Remaining(actionLock.UnlockedAtFrame, now);
}
