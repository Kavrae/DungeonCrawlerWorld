using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Spawning;

namespace Game.World;

/// <summary>Moves an entity straight to another cell of the loaded map, without walking there.</summary>
/// <remarks>
/// <para>
/// The one teleport path: gameplay and Admin Mode's "Teleport here" both use it. The move is
/// recorded through SpawnMoves, so it reaches every reader of the frame's moves (tiers, auras,
/// contact damage) whether it happens between frames, mid-frame or from Presentation, and it is
/// published on the EventBus like the player's own moves.
/// </para>
/// <para>
/// A teleport drops the entity's movement destination and cancels a windup in progress: both were
/// aimed from the cell it left. A skeleton is built first, like any other gameplay write that
/// reaches one.
/// </para>
/// <para>
/// A teleport never leaves the loaded map; a destination outside it is refused (see the
/// "Long-range teleports" TODO).
/// </para>
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityTeleporter(World world, DirectComponentPool<TransformComponent> transforms, SpawnMoves moves, EventBus eventBus, CreatureSkeletons skeletons, PackedComponentPool<MovementComponent> movements, PackedComponentPool<PendingDelayedActionComponent> pendingActions)
{

    /// <summary>Whether TryTeleport would move entityId to destination: entityId is on the map, and destination is another cell it could stand in (on the loaded map, not blocked, not occupied).</summary>
    public bool CanTeleport(int entityId, Vector3Int destination) =>
        transforms.TryGetReadonly(entityId, out var transform) &&
        world.IsOnMap(transform.Position) &&
        transform.Position != destination &&
        MovementCandidates.CanOccupy(world, destination, transform.Size, entityId, world.IsBlocking(entityId));

    /// <summary>Teleports entityId to destination.</summary>
    /// <returns>False, changing nothing, when CanTeleport is false.</returns>
    public bool TryTeleport(int entityId, Vector3Int destination)
    {
        if (!CanTeleport(entityId, destination))
        {
            return false;
        }

        skeletons.EnsureBuilt(entityId);

        ref var liveTransform = ref transforms.Get(entityId);
        var originPosition = liveTransform.Position;
        world.MoveEntity(entityId, destination, liveTransform);
        liveTransform.Position = destination;

        movements.TryUpdate(entityId, static (ref MovementComponent movement) =>
        {
            movement.TargetMapPosition = null;
            movement.NextMapPosition = null;
        });
        pendingActions.Remove(entityId);

        var teleportMove = new EntityMovedEvent(entityId, originPosition, destination, liveTransform.Size);
        moves.Record(teleportMove);
        eventBus.Publish(teleportMove);
        return true;
    }
}
