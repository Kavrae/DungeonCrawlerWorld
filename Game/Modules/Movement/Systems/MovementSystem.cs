using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.Movement.Systems;

/// <summary>
/// Executes whatever move an entity's MovementComponent.NextMapPosition already asks for --
/// purely reactive, not a decision-maker. Player-controlled moves are queued externally
/// (Presentation input); Random-mode wandering is now decided upstream by
/// TestCombatBehaviorSystem (Game.Modules.NpcBehavior), which runs before this system every
/// frame (see GameBootstrapper's module order) -- this system only ever executes a destination,
/// never picks one (see MovementCandidates for the position-candidate math both still share).
/// Depends on IMapQuery, not the concrete World, for collision/bounds reads. A confirmed move is
/// delivered three ways, matched to each consumer's actual need (see a gameplay-demo profiling
/// investigation that found the old single EventBus.Publish -- fanning out to every subscriber,
/// synchronously, per move, across a striped ~150k-entity population -- was a measured hotspot):
/// - entityMoveSync.SyncMove: direct call, not the bus. Map-occupancy bookkeeping is
///   mandatory, not an optional module reaction, so it doesn't belong on EventBus at all (see
///   IEntityMoveSync's own doc comment).
/// - movedEntities.Record: a per-frame buffer ContactDamageSystem/StatusEffectAuraSystem drain
///   during their own Update, instead of each subscribing to a per-move event (see
///   FrameEventBuffer's own doc comment).
/// - eventBus.Publish(EntityMovedEvent), only for the player's own move: PlayerActivityLog still
///   subscribes to this on the bus exactly as before, just now firing at player-move frequency
///   (a handful/sec) instead of the full population's.
/// </summary>
public sealed class MovementSystem : ISystem
{
    private const byte StripeCountValue = 15;

    /// <summary>√2 -- a diagonal step covers that much more distance than a cardinal one, so it sets the shared ActionLock for proportionally longer.</summary>
    private const float DiagonalActionLockMultiplier = 1.41421356f;

    public byte StripeCount => StripeCountValue;

    private readonly DirectComponentPool<TransformComponent> _transformComponents;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<MovementComponent> _movementComponents;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly IEntityMoveSync _entityMoveSync;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly PackedComponentPool<PendingAbilityActivationComponent>? _pendingAbilityActivations;
    private readonly PackedComponentPool<PendingConsumableActivationComponent>? _pendingConsumableActivations;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    public MovementSystem(
        DirectComponentPool<TransformComponent> transformComponents,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<MovementComponent> movementComponents,
        IMapQuery mapQuery,
        EventBus eventBus,
        IEntityMoveSync entityMoveSync,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null,
        PackedComponentPool<PendingAbilityActivationComponent>? pendingAbilityActivations = null,
        PackedComponentPool<PendingConsumableActivationComponent>? pendingConsumableActivations = null)
    {
        _transformComponents = transformComponents;
        _actionLocks = actionLocks;
        _movementComponents = movementComponents;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _entityMoveSync = entityMoveSync;
        _movedEntities = movedEntities;
        _playerQuery = playerQuery;
        _deadEntities = deadEntities;
        _pendingAbilityActivations = pendingAbilityActivations;
        _pendingConsumableActivations = pendingConsumableActivations;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementComponents, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _tieredStripeSet.GetDueEntities(time.FrameCount))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            ref readonly var movementComponent = ref _movementComponents.GetReadonly(entityId);

            if (movementComponent.FramesToWait > 0)
            {
                _movementComponents.TryUpdate(entityId, static (ref MovementComponent movementComponent) =>
                {
                    movementComponent.FramesToWait = (short)Math.Max(0, movementComponent.FramesToWait - StripeCountValue);
                });
                continue;
            }

            if (ActionLockGate.IsBlocked(_actionLocks, entityId) ||
                !_transformComponents.TryGetReadonly(entityId, out var transformComponent))
            {
                continue;
            }

            if (!_mapQuery.IsOnMap(transformComponent.Position))
            {
                continue;
            }

            // Something upstream (TestCombatBehaviorSystem) already decided this entity's turn
            // this frame via a queued ability/consumable activation -- don't also try to move
            // it. Requires TestCombatBehaviorSystem to run earlier in the frame (see
            // GameBootstrapper's module order) so this check sees the same-frame request.
            if (_pendingAbilityActivations?.Has(entityId) == true || _pendingConsumableActivations?.Has(entityId) == true)
            {
                continue;
            }

            var justSelected = movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value;
            if (justSelected)
            {
                ClearArrivedDestinationIfIdle(entityId);
            }

            if (movementComponent.NextMapPosition != null)
            {
                TryMoveToNextMapPosition(entityId, movementComponent, transformComponent);
            }
        }
    }

    /// <summary>
    /// Reaching here means the entity has arrived at NextMapPosition (or never had one) with
    /// nothing new queued upstream this frame -- see this method's only caller. Nothing in this
    /// system auto-picks a new destination for any mode anymore: Player-controlled moves are
    /// queued externally (Presentation input), and Random-mode wandering is decided upstream by
    /// TestCombatBehaviorSystem, which runs before this system each frame. So NextMapPosition
    /// just needs clearing here -- otherwise it stays set to the position the entity is already
    /// standing on, and the very next Update call where the action lock allows it would re-run
    /// TryMoveToNextMapPosition against that same value: a same-position "move" that sets the
    /// action lock and publishes a spurious EntityMovedEvent(old == new) every cycle, repeating
    /// forever instead of the entity staying put until a real move is queued.
    /// </summary>
    private void ClearArrivedDestinationIfIdle(int entityId) =>
        _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);

    /// <summary>
    /// Attempts to move toward the selected node. MovementCandidates.CanOccupy is always
    /// re-checked here in case another entity has moved into the space since NextMapPosition was
    /// chosen -- whether that choice happened this same frame (TestCombatBehaviorSystem's wander
    /// decision) or was carried over from an earlier one (Player-controlled's externally-queued
    /// moves); either way, time -- and other entities' moves -- may have passed since it was
    /// picked. The action lock is set on the move itself, not during path selection.
    /// </summary>
    private void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
    {
        var newPosition = movementComponent.NextMapPosition!.Value;
        var oldPosition = transformComponent.Position;
        var isBlocking = _mapQuery.IsBlocking(entityId);

        if (!MovementCandidates.CanOccupy(_mapQuery, newPosition, transformComponent.Size, entityId, isBlocking) ||
            !MovementCandidates.IsDiagonalMoveClear(_mapQuery, oldPosition, newPosition, transformComponent.Size, entityId, isBlocking))
        {
            _movementComponents.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);
            return;
        }

        if (_transformComponents.TryUpdate(entityId, newPosition, static (ref transformComponent, newPosition) =>
        {
            transformComponent.Position = newPosition;
        }))
        {
            var isDiagonal = newPosition.X != oldPosition.X && newPosition.Y != oldPosition.Y;
            var lockFrames = isDiagonal
                ? (short)MathF.Round(movementComponent.ActionCooldownFrames * DiagonalActionLockMultiplier)
                : movementComponent.ActionCooldownFrames;

            ActionLockGate.Lock(_actionLocks, entityId, lockFrames);

            var moved = new EntityMovedEvent(entityId, oldPosition, newPosition, transformComponent.Size);
            _entityMoveSync.SyncMove(moved);
            _movedEntities.Record(moved);

            if (entityId == _playerQuery?.PlayerEntityId)
            {
                _eventBus.Publish(moved);
            }
        }
    }
}
