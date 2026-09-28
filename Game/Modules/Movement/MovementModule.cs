using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.World;
using Game.Modules.Death;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatModifiers;
using Game.Modules.BodyPartEffects;

namespace Game.Modules.Movement;

/// <summary>The movement module for handling entity movement logic.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MovementModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000004");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, DeathModule.ModuleId, StatusEffectAuraModule.ModuleId, StatModifiersModule.ModuleId, BodyPartEffectsModule.ModuleId, ProcessingTierModule.ModuleId];

    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private IEntityMoveSync? _entityMoveSync;
    private FrameEventBuffer<EntityMovedEvent> _movedEntities = null!;
    private IPlayerQuery _playerQuery = null!;
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
            existing.MovementMode = (MovementMode)Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode);
            // The later deadline wins rather than averaging -- averaging two absolute frames would
            // invent a moment neither part asked for, the same rule ActionLockComponent's own merge uses.
            existing.WaitUntilFrame = Math.Max(existing.WaitUntilFrame, incoming.WaitUntilFrame);
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

        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        // Only used to widen MovementSystem's EventBus.Publish gate to an aura-carrying mover (see
        // MovementSystem's own doc comment).
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var movementDisabled = componentManager.GetPackedPool<MovementDisabledComponent>();

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
            auraSources,
            statModifiers,
            movementDisabled));

        systemManager.RegisterFrameScoped(_movedEntities);
    }
}