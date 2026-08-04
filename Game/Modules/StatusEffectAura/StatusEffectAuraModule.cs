using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffectAura.Systems;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// "Radiates a status-effect aura" support -- Lava is the first user (Burning, Strength 8).
/// Fully generic over StatusEffectType end to end, harmful or beneficial: the source/
/// exposure/grid machinery never knew about any specific effect, and now neither does
/// stack-granting -- StatusEffectAuraSystem.GrantStacks dispatches through the shared
/// StatusEffectAuraApplierRegistry (see IStatusEffectAuraApplier), populated by each concrete
/// effect module's own Configure call (BurningModule/PoisonModule each register a
/// TimerBasedAuraApplier&lt;T&gt; for their own timer component). This module's own Dependencies
/// list StatusEffectsModule (shared stack storage) and, now, MovementModule -- the latter so
/// StatusEffectAuraSystem's own Update always runs after MovementSystem's within the same
/// SystemManager.Update() cycle, required for it to see this frame's moves via the shared
/// FrameEventBuffer&lt;EntityMoved&gt; (see that class's own doc comment on why
/// producer-before-consumer ordering matters). Parameterless, with runtime dependencies
/// (IMapQuery, the applier registry, the moved-entities buffer) supplied via
/// IGameModule.Configure.
/// </summary>
public sealed class StatusEffectAuraModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000b");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule), typeof(MovementModule)];

    private IMapQuery _mapQuery = null!;
    private StatusEffectAuraApplierRegistry _applierRegistry = null!;
    private FrameEventBuffer<EntityMoved> _movedEntities = null!;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _applierRegistry = context.StatusEffectAuraAppliers;
        _movedEntities = context.MovedEntities;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<StatusEffectAuraSourceComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<StatusEffectAuraExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;

        systemManager.Register(new StatusEffectAuraSystem(
            componentManager,
            componentManager.GetPackedPool<StatusEffectAuraExposureComponent>(),
            componentManager.GetPackedPool<StatusEffectAuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            _mapQuery,
            _applierRegistry,
            _movedEntities,
            deadEntities));
    }
}
