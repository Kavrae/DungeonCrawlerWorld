using Engine.Events;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>
/// Keeps World.Map's per-cell entity index in sync with MovementSystem's confirmed moves,
/// via EntityMoved rather than a direct reference MovementSystem would otherwise need to
/// hold. Subscribing in the constructor is enough to keep this instance alive for as long as
/// the EventBus is -- a bound instance-method delegate rooted in the subscriber list keeps
/// its target alive, so nothing needs to hold a reference to this afterward.
/// </summary>
public sealed class WorldEventSync
{
    private readonly World _world;

    public WorldEventSync(World world, EventBus eventBus)
    {
        _world = world;
        eventBus.Subscribe<EntityMoved>(OnEntityMoved);
    }

    private void OnEntityMoved(EntityMoved moved) =>
        _world.MoveEntity(moved.EntityId, moved.NewPosition, new TransformComponent(moved.OldPosition, moved.Size));
}
