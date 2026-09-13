using Engine.ECS.Systems;
using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
/// <remarks>
/// WaitUntilFrame is a deadline, not a countdown: nothing advances it, and every reader compares it
/// against the current simulation frame (FrameDeadline.IsReached). No system visits an entity for
/// its wait to elapse, so a waiting entity at any processing tier resumes on exactly the frame it
/// should -- the defect the old per-visit decrement kept reintroducing. See PLAN-timer-wheel.md
/// step 8. It is deliberately not on a timer wheel: nothing fires when it elapses, it only stops
/// gating.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public struct MovementComponent(MovementMode movementMode, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
{
    /// <summary>The movement or pathfinding mode of the entity</summary>
    public MovementMode MovementMode { get; set; } = movementMode;

    /// <summary>Movement's own private retry backoff -- the frame this entity may next attempt to move on. 0 means "not waiting".</summary>
    /// <remarks>Set when a MovementMode.Random entity finds every direction blocked, so it doesn't re-run the same failed search every activation. Read as a gate by MovementSystem and TestCombatBehaviorSystem; written by whichever of them found no options.</remarks>
    public uint WaitUntilFrame { get; set; } = 0;

    /// <summary>The 3D position the entity is pathing toward.</summary>
    public Vector3Int? TargetMapPosition { get; set; } = targetMapPosition;

    /// <summary>The map node to attempt to move to next, as a step toward TargetMapPosition -- separated out to allow delayed/recalculated movement.</summary>
    public Vector3Int? NextMapPosition { get; set; } = nextMapPosition;

    /// <summary>True while this entity is still serving its retry backoff as of now.</summary>
    public readonly bool IsWaiting(long now) => !FrameDeadline.IsReached(WaitUntilFrame, now);

    public override readonly string ToString() => $"Mode : {MovementMode}\nWaitUntilFrame : {WaitUntilFrame}\nTargetMapPosition : {TargetMapPosition}\nNextMapPosition : {NextMapPosition}";
}
