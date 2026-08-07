using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.World;

namespace Game.Modules.Death.Systems;

/// <summary>
/// The sole handler for EntityDiedEvent: subscribes once at construction, then Update() just drains
/// whatever queued up this frame via DispatchBuffered -- see EntityDiedEvent's own doc comment for
/// why this indirection (never handling it inline from whichever system published it) matters.
/// No population of its own to stripe over (nothing to iterate every frame besides the queue
/// itself), so StripeCount is 1 and there's no EntityStripeSet.
/// </summary>
public sealed class DeathSystem : ISystem
{
    public byte StripeCount => 1;

    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly MultiComponentPool<NonBlockingComponent> _nonBlockingEntities;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly IEntityMoveSync _entityMoveSync;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;

    public DeathSystem(
        PackedComponentPool<DeadComponent> deadEntities,
        MultiComponentPool<NonBlockingComponent> nonBlockingEntities,
        DirectComponentPool<TransformComponent> transforms,
        IEntityMoveSync entityMoveSync,
        IMapQuery mapQuery,
        EventBus eventBus)
    {
        _deadEntities = deadEntities;
        _nonBlockingEntities = nonBlockingEntities;
        _transforms = transforms;
        _entityMoveSync = entityMoveSync;
        _mapQuery = mapQuery;
        _eventBus = eventBus;

        eventBus.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    public void Update(EngineTime time, byte stripeIndex) => _eventBus.DispatchBuffered<EntityDiedEvent>();

    /// <summary>Defensive against a duplicate EntityDiedEvent for the same entity -- HealthDamage.Apply's own wasAlive transition guard should already make this unreachable, but a corpse should never be double-processed if it somehow happens.</summary>
    private void OnEntityDied(EntityDiedEvent died)
    {
        if (_deadEntities.Has(died.EntityId))
        {
            return;
        }

        // Only a currently-Blocking entity needs converting -- an already-non-Blocking one
        // (e.g. a Phasing Ghost) is already correctly indexed/positioned and may be sharing
        // its tile with a real Blocking occupant, so touching Map's Blocking slot for it would
        // incorrectly clear that other occupant's registration (see ConvertToNonBlocking's own
        // doc comment). Either way the corpse stays fully in place -- findable/renderable at
        // its death position, just no longer physically blocking -- unlike a full despawn.
        if (_mapQuery.IsBlocking(died.EntityId))
        {
            ref var transform = ref _transforms.Get(died.EntityId);
            _nonBlockingEntities.Add(died.EntityId, new NonBlockingComponent());
            _entityMoveSync.ConvertToNonBlocking(died.EntityId, ref transform);
        }

        var killedBy = died.Source.Kind == StatusEffectSourceKind.Entity
            ? died.Source.EntityId
            : (int?)null;
        _deadEntities.Add(died.EntityId, new DeadComponent(killedBy));
    }
}
