using Engine.Math;

namespace Game.World;

/// <summary>
/// Published by MovementSystem after it confirms a move (TransformComponent.Position is
/// already updated by the time this fires). Deliberately immediate, not IBufferedEvent:
/// WorldEventSync's subscriber must update World.Map's node index before the next entity's
/// collision check this same frame, or two entities could move into the same cell.
/// </summary>
public readonly record struct EntityMoved(int EntityId, Vector3Int OldPosition, Vector3Int NewPosition, Vector3Byte Size);
