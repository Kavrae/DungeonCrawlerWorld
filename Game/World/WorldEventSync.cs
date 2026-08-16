using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// Keeps World.Map's per-cell entity index in sync with MovementSystem's confirmed moves.
/// Implements IEntityMoveSync so MovementSystem can call SyncMove directly (mandatory
/// bookkeeping, not an optional module reaction) instead of going through EventBus -- see
/// IEntityMoveSync's own doc comment. Calls World.MoveEntityUnchecked, not the public
/// MoveEntity, since the caller (MovementSystem) has already validated the destination
/// footprint via its own CanMove moments earlier in the same call.
/// </summary>
public sealed class WorldEventSync(World world) : IEntityMoveSync
{
    private readonly World _world = world;

    public void SyncMove(EntityMovedEvent moved, bool isBlocking) =>
        _world.MoveEntityUnchecked(moved.EntityId, moved.NewPosition, new TransformComponent(moved.OldPosition, moved.Size), isBlocking);

    public void ConvertToNonBlocking(int entityId, ref TransformComponent transform) =>
        _world.ConvertToNonBlocking(entityId, ref transform);
}
