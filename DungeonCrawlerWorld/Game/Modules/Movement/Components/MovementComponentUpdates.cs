using Engine.ECS.Components;
using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>Sets the map node the entity attempts to move to next; null stops it.</summary>
internal readonly struct SetNextMapPositionUpdate(Vector3Int? nextMapPosition) : IComponentUpdater<MovementComponent>
{
    public void Update(ref MovementComponent movement) => movement.NextMapPosition = nextMapPosition;
}

/// <summary>Sets the frame the entity may next attempt to move on.</summary>
internal readonly struct SetWaitUntilFrameUpdate(uint waitUntilFrame) : IComponentUpdater<MovementComponent>
{
    public void Update(ref MovementComponent movement) => movement.WaitUntilFrame = waitUntilFrame;
}

/// <summary>Sets how the entity chooses where to move.</summary>
internal readonly struct SetMovementModeUpdate(MovementMode movementMode) : IComponentUpdater<MovementComponent>
{
    public void Update(ref MovementComponent movement) => movement.MovementMode = movementMode;
}

/// <summary>Clears both where the entity is pathing to and its next step.</summary>
internal readonly struct ClearMovementTargetsUpdate : IComponentUpdater<MovementComponent>
{
    public void Update(ref MovementComponent movement)
    {
        movement.TargetMapPosition = null;
        movement.NextMapPosition = null;
    }
}
