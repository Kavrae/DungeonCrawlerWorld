using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
/// <remarks>What it does next is written by whoever decides for it, through IntentWriter: one step at a time, taken by MovementSystem once the action lock allows.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public struct MovementComponent(Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
{
    /// <summary>The 3D position the entity is pathing toward.</summary>
    public Vector3Int? TargetMapPosition
    {
        readonly get => PositionOrNull(_targetMapPosition);
        set => _targetMapPosition = value ?? NoPosition;
    }

    /// <summary>The map node to attempt to move to next, as a step toward TargetMapPosition -- separated out to allow delayed/recalculated movement.</summary>
    public Vector3Int? NextMapPosition
    {
        readonly get => PositionOrNull(_nextMapPosition);
        set => _nextMapPosition = value ?? NoPosition;
    }

    /// <summary>Held as a sentinel position rather than a Vector3Int?, which pays 4 bytes of padding for its flag -- the same trick TransformComponent.UnplacedOn already uses for "not on the map".</summary>
    private Vector3Int _targetMapPosition = targetMapPosition ?? NoPosition;

    /// <inheritdoc cref="_targetMapPosition"/>
    private Vector3Int _nextMapPosition = nextMapPosition ?? NoPosition;

    /// <summary>The position that means "none": far outside any map, since every small coordinate is a real tile.</summary>
    private static readonly Vector3Int NoPosition = new(TransformComponent.UnplacedCoordinate, TransformComponent.UnplacedCoordinate, 0);

    private static Vector3Int? PositionOrNull(Vector3Int position) => position.X == TransformComponent.UnplacedCoordinate ? null : position;

    public override readonly string ToString() => $"TargetMapPosition : {TargetMapPosition}\nNextMapPosition : {NextMapPosition}";
}
