using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
/// <cleanupVersion>1</cleanupVersion>
public struct MovementComponent(MovementMode movementMode, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
{
    /// <summary>The movement or pathfinding mode of the entity</summary>
    public MovementMode MovementMode { get; set; } = movementMode;

    /// <summary>Movement's own private retry backoff. </summary>
    /// <remarks>Set when MovementMode.Random entity finds every direction blocked, so it doesn't re-run the same failed search every activation.</remarks>
    public ushort FramesToWait { get; set; } = 0;

    /// <summary>The 3D position the entity is pathing toward.</summary>
    public Vector3Int? TargetMapPosition { get; set; } = targetMapPosition;

    /// <summary>The map node to attempt to move to next, as a step toward TargetMapPosition -- separated out to allow delayed/recalculated movement.</summary>
    public Vector3Int? NextMapPosition { get; set; } = nextMapPosition;

    public override readonly string ToString() => $"Mode : {MovementMode}\nFramesToWait : {FramesToWait}\nTargetMapPosition : {TargetMapPosition}\nNextMapPosition : {NextMapPosition}";
}