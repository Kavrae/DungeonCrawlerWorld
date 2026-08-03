using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>
/// Burning-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared stack storage) and HealthModule (what it damages). Parameterless, with runtime
/// dependencies (EventBus, IPlayerQuery) supplied via IGameModule.Configure. Also registers a
/// TimerBasedAuraApplier&lt;BurningTimerComponent&gt; into the shared
/// StatusEffectAuraApplierRegistry during Configure, so StatusEffectAuraSystem can grant
/// Burning stacks without depending on this module directly.
/// </summary>
public sealed class BurningModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000008");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule)];

    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        context.StatusEffectAuraAppliers.Register(new TimerBasedAuraApplier<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.ApplyStack));
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;

        systemManager.Register(new BurningSystem(
            componentManager.GetPackedPool<BurningTimerComponent>(),
            componentManager.GetMultiPool<StatusEffectStack>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _eventBus,
            _playerQuery,
            statModifiers));
    }
}
