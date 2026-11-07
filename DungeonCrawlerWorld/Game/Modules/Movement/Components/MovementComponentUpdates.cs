using Engine.ECS.Components;
using Engine.Math;

namespace Game.Modules.Movement.Components;

/// <summary>Sets the map node the entity attempts to move to next; null stops it.</summary>
internal readonly struct SetNextMapPositionUpdate(Vector3Int? nextMapPosition) : IComponentUpdater<MovementComponent>
{
    public void Update(ref MovementComponent movement) => movement.NextMapPosition = nextMapPosition;
}
