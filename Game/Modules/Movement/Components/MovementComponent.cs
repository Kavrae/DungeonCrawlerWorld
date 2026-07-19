using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
public struct MovementComponent(MovementMode movementMode, short energyToMove, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
{
    public MovementMode MovementMode { get; set; } = movementMode;
    public short EnergyToMove { get; set; } = energyToMove;
    public short FramesToWait { get; set; } = 0;

    /// <summary>The 3D position the entity is pathing toward.</summary>
    public Vector3Int? TargetMapPosition { get; set; } = targetMapPosition;

    /// <summary>The map node to attempt to move to next, as a step toward TargetMapPosition -- separated out to allow delayed/recalculated movement.</summary>
    public Vector3Int? NextMapPosition { get; set; } = nextMapPosition;

    public override readonly string ToString() => $"Movement : {MovementMode}";
}
