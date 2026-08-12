using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Actions.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.World;

namespace Game.Modules.Movement;

/// <summary>
/// Parameterless (required for runtime discovery -- see decision #1/#2 in the modding plan)
/// with its runtime dependencies (IMapQuery, EventBus) supplied via IGameModule.Configure
/// instead of the constructor.
/// </summary>
public sealed class MovementModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000004");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(CoreModule)];

    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private IEntityMoveSync? _entityMoveSync;
    private FrameEventBuffer<EntityMovedEvent> _movedEntities = null!;
    private IPlayerQuery? _playerQuery;
    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _entityMoveSync = context.EntityMoveSync;
        _movedEntities = context.MovedEntities;
        _playerQuery = context.PlayerQuery;
        _processingTierEvents = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) =>
        {
            existing.MovementMode = (MovementMode)System.Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode);
            existing.ActionCooldownFrames = (short)((existing.ActionCooldownFrames + incoming.ActionCooldownFrames) / 2);
            existing.FramesToWait = (short)((existing.FramesToWait + incoming.FramesToWait) / 2);
            existing.NextMapPosition = incoming.NextMapPosition;
            existing.TargetMapPosition = incoming.TargetMapPosition;
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_entityMoveSync is null)
        {
            throw new InvalidOperationException($"{nameof(MovementModule)} requires {nameof(GameModuleContext)}.{nameof(GameModuleContext.EntityMoveSync)} to be set.");
        }

        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var pendingActionActivations = componentManager.IsRegistered<PendingActionActivationComponent>()
            ? componentManager.GetPackedPool<PendingActionActivationComponent>()
            : null;
        var pendingConsumableActivations = componentManager.IsRegistered<PendingConsumableActivationComponent>()
            ? componentManager.GetPackedPool<PendingConsumableActivationComponent>()
            : null;
        // Soft, IsRegistered-guarded dependency on StatusEffectAuraModule's own component --
        // MovementModule can't take a hard Dependencies entry on it, since StatusEffectAuraModule
        // itself already depends on MovementModule (see that module's own doc comment), so a
        // hard dependency the other way would be circular. Mirrors DeathModule's identical soft
        // dependency on the same component. Only used to widen MovementSystem's EventBus.Publish
        // gate to an aura-carrying mover (see MovementSystem's own doc comment) -- MovementModule
        // doesn't otherwise need anything from that module.
        var auraSources = componentManager.IsRegistered<StatusEffectAuraSourceComponent>()
            ? componentManager.GetMultiPool<StatusEffectAuraSourceComponent>()
            : null;

        systemManager.Register(new MovementSystem(
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            _mapQuery,
            _eventBus,
            _entityMoveSync,
            _movedEntities,
            _playerQuery,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            deadEntities,
            pendingActionActivations,
            pendingConsumableActivations,
            auraSources));

        systemManager.RegisterFrameScoped(_movedEntities);
    }
}