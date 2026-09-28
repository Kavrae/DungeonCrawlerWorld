using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffectAura.Systems;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Modules.Core;
using Game.Modules.Death;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// "Radiates a status-effect aura" support -- Lava is the first user (Burning, Strength 8).
/// Fully generic over StatusEffectType end to end, harmful or beneficial: the source/
/// exposure/grid machinery never knew about any specific effect, and now neither does
/// stack-granting -- StatusEffectAuraSystem.GrantStacks dispatches through the shared
/// StatusEffectAuraApplierRegistry (see IStatusEffectAuraApplier), populated by each concrete
/// effect module's own Configure call (BurningModule/PoisonModule each register a
/// TimerBasedAuraApplier&lt;T&gt; for their own timer component). This module requires
/// StatusEffectsModule (shared stack storage) and runs after MovementModule, so
/// StatusEffectAuraSystem's own Update always runs after MovementSystem's within the same
/// SystemManager.Update() cycle, required for it to see this frame's moves via the shared
/// FrameEventBuffer&lt;EntityMovedEvent&gt; (see that class's own doc comment on why
/// producer-before-consumer ordering matters). Parameterless, with runtime dependencies
/// (IMapQuery, the applier registry, the moved-entities buffer) supplied via
/// IGameModule.Configure.
/// </summary>
public sealed class StatusEffectAuraModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000b");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatusEffectsModule.ModuleId, CoreModule.ModuleId, DeathModule.ModuleId, ProcessingTierModule.ModuleId];

    public IReadOnlyList<Guid> RunsAfter { get; } = [MovementModule.ModuleId];

    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private StatusEffectAuraApplierRegistry _applierRegistry = null!;
    private FrameEventBuffer<EntityMovedEvent> _movedEntities = null!;
    private ProcessingTierEvents _processingTierEvents = null!;
    private SimulationClock _simulationClock = null!;
    private SimulationScope _simulationScope = null!;
    private Terrain.TerrainRegistry _terrain = null!;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _applierRegistry = context.StatusEffectAuraAppliers;
        _movedEntities = context.MovedEntities;
        _processingTierEvents = context.ProcessingTierEvents;
        _simulationClock = context.SimulationClock;
        _simulationScope = context.SimulationScope;
        _terrain = context.Terrain;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<StatusEffectAuraSourceComponent>();
        componentManager.RegisterMultiPool<StatusEffectAuraExposureComponent>();
        componentManager.RegisterPackedPool<AuraSourceExpiryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();

        systemManager.Register(new StatusEffectAuraSystem(
            componentManager,
            componentManager.GetMultiPool<StatusEffectAuraExposureComponent>(),
            componentManager.GetMultiPool<StatusEffectAuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            _mapQuery,
            _eventBus,
            _applierRegistry,
            _movedEntities,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            _simulationClock,
            _terrain,
            deadEntities,
            _simulationScope));

        systemManager.Register(new AuraSourceExpirySystem(
            componentManager.GetPackedPool<AuraSourceExpiryComponent>(),
            componentManager.GetMultiPool<StatusEffectAuraSourceComponent>(),
            _eventBus,
            _simulationScope));
    }
}
