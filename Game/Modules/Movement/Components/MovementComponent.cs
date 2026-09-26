using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Modules.Movement.Components;

/// <summary>An entity's ability to move through the map.</summary>
/// <remarks>
/// WaitUntilFrame is a deadline, not a countdown: nothing advances it, and every reader compares it
/// against the current simulation frame (FrameDeadline.IsReached). No system visits an entity for
/// its wait to elapse, so a waiting entity at any processing tier resumes on exactly the frame it
/// should -- the defect the old per-visit decrement kept reintroducing. It is deliberately not on a
/// timer wheel: nothing fires when it elapses, it only stops
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

    /// <summary>True while this entity is still serving its retry backoff as of now.</summary>
    public readonly bool IsWaiting(long now) => !FrameDeadline.IsReached(WaitUntilFrame, now);

    public override readonly string ToString() => $"Mode : {MovementMode}\nWaitUntilFrame : {WaitUntilFrame}\nTargetMapPosition : {TargetMapPosition}\nNextMapPosition : {NextMapPosition}";
}
