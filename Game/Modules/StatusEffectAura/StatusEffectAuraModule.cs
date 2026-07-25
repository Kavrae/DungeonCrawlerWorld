using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
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
/// TimerBasedAuraApplier&lt;T&gt; for their own timer component). This module therefore only depends on
/// StatusEffectsModule (shared stack storage) -- not on any one concrete effect module -- so a
/// brand new effect type just needs its own IStatusEffectAuraApplier registered, nothing here
/// changes. Parameterless, with runtime dependencies (EventBus, IMapQuery, the applier
/// registry) supplied via IGameModule.Configure.
/// </summary>
public sealed class StatusEffectAuraModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000b");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule)];

    private EventBus _eventBus = null!;
    private IMapQuery _mapQuery = null!;
    private StatusEffectAuraApplierRegistry _applierRegistry = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _mapQuery = context.MapQuery;
        _applierRegistry = context.StatusEffectAuraAppliers;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<StatusEffectAuraSourceComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<StatusEffectAuraExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        systemManager.Register(new StatusEffectAuraSystem(
            componentManager,
            componentManager.GetPackedPool<StatusEffectAuraExposureComponent>(),
            componentManager.GetPackedPool<StatusEffectAuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            _mapQuery,
            _eventBus,
            _applierRegistry));
}
