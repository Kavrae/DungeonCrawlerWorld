using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Poison;

/// <summary>
/// Poison-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared stack storage). Parameterless, with runtime dependencies (EventBus, IPlayerQuery)
/// supplied via IGameModule.Configure.
/// </summary>
public sealed class PoisonModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000009");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule)];

    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        systemManager.Register(new PoisonSystem(
            componentManager.GetPackedPool<PoisonTimerComponent>(),
            componentManager.GetMultiPool<StatusEffectStack>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _eventBus,
            _playerQuery));
    }
}
