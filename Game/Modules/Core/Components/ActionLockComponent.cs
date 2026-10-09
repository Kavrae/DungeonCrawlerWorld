using Engine.ECS.Systems;

namespace Game.Modules.Core.Components;

/// <summary>Action-prevention lock shared across all immediate and delayed actions, plus movement.</summary>
/// <remarks>
/// A deadline, not a countdown: nothing ticks this down -- readers compare UnlockedAtFrame against
/// the current simulation frame through ActionLockGate, so an entity costs nothing at all while
/// locked, at any processing tier. CurrentLockTotalFrames is kept alongside
/// it purely as the denominator of the UI's "how far through the windup" fraction (see
/// ActionLockContent/HotbarContent/MapWindow's charge fill). How long an entity's standard lock is
/// isn't stored here: it comes from its Dexterity and modifiers (StandardActionLockFrames).
/// </remarks>
/// <param name="currentLockTotalFrames">How long the current lock was set for, in frames -- the denominator UI fills divide by.</param>
/// <param name="unlockedAtFrame">The simulation frame the entity can act again on -- FrameDeadline.After(now, lockFrames). 0 means unlocked.</param>
/// <cleanupVersion>1</cleanupVersion>
public struct ActionLockComponent(ushort currentLockTotalFrames, uint unlockedAtFrame)
{
    public ushort CurrentLockTotalFrames { get; set; } = currentLockTotalFrames;

    /// <summary>The simulation frame this entity is free to act again on. Reached (or 0) means unlocked -- see ActionLockGate.IsBlocked.</summary>
    public uint UnlockedAtFrame { get; set; } = unlockedAtFrame;

    public override readonly string ToString() => $"Unlocked at frame {UnlockedAtFrame} ({CurrentLockTotalFrames} total)";
}
