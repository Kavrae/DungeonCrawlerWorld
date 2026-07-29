using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
public struct MovementComponent(MovementMode movementMode, short actionCooldownFrames, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
{
    public MovementMode MovementMode { get; set; } = movementMode;

    /// <summary>
    /// How many real game frames the shared ActionLockComponent is set to on a successful move
    /// </summary>
    public short ActionCooldownFrames { get; set; } = actionCooldownFrames;

    /// <summary>
    /// Movement's own private retry backoff, in real game frames -- distinct from the shared
    /// ActionLockComponent. Set (to MovementSystem.FramesToWaitIfNoOptions) when a
    /// MovementMode.Random entity finds every direction blocked, so it doesn't re-run the same
    /// failed search every time it's otherwise eligible to act.
    /// </summary>
    public short FramesToWait { get; set; } = 0;

    /// <summary>The 3D position the entity is pathing toward.</summary>
    public Vector3Int? TargetMapPosition { get; set; } = targetMapPosition;

    /// <summary>The map node to attempt to move to next, as a step toward TargetMapPosition -- separated out to allow delayed/recalculated movement.</summary>
    public Vector3Int? NextMapPosition { get; set; } = nextMapPosition;

    public override readonly string ToString() => $"Movement : {MovementMode}";
}