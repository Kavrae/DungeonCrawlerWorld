using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.Abilities;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (AbilityCatalog,
/// IMapQuery, EventBus, IPlayerQuery) supplied via IGameModule.Configure instead of the
/// constructor.
/// </summary>
public sealed class AbilitiesModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000c");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private AbilityCatalog _abilityCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _abilityCatalog = context.Abilities;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(
            static (ref PendingDelayedActionComponent existing, PendingDelayedActionComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingAbilityActivationComponent>(
            static (ref PendingAbilityActivationComponent existing, PendingAbilityActivationComponent incoming) => existing = incoming);
        componentManager.RegisterMultiPool<HotkeyBindingComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new AbilityCooldownSystem(componentManager.GetMultiPool<AbilityInstanceComponent>()));

        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _abilityCatalog,
            _mapQuery,
            _eventBus,
            _playerQuery));

        systemManager.Register(new AbilityActivationSystem(
            componentManager.GetPackedPool<PendingAbilityActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _abilityCatalog,
            _mapQuery,
            _eventBus,
            _playerQuery));
    }
}
