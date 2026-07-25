using Engine.Math;

namespace Game.World;

/// <summary>
/// Published by MovementSystem after it confirms a move (TransformComponent.Position is
/// already updated by the time this fires). Deliberately immediate, not IBufferedEvent:
/// WorldEventSync's subscriber must update World.Map's node index before the next entity's
/// collision check this same frame, or two entities could move into the same cell.
///
/// Also published (with OldPosition == NewPosition, a "moved from nowhere to here" sentinel)
/// by anything that spawns an entity directly via World.PlaceEntityOnMap, immediately after
/// placing it -- e.g. FloorBuilder.CreatePlayer. WorldEventSync's handler tolerates Old ==
/// New harmlessly (clears then immediately re-sets the same cell). Without this, an entity
/// spawned directly onto/next to a hazard (ContactDamageSystem/StatusEffectAuraSystem, both
/// EntityMoved-driven) would stay undetected until it next actually moved. Any future spawn
/// path (a monster spawner, once one exists) must do the same.
/// </summary>
public readonly record struct EntityMoved(int EntityId, Vector3Int OldPosition, Vector3Int NewPosition, Vector2Byte Size);