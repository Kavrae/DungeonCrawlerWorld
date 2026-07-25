using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.ContactDamage;

/// <summary>
/// Generic "damage whatever stands on me" hazard support -- Lava is the first user
/// (DamagePerTick: 10, TickIntervalFrames: 60), but nothing here is lava-specific.
/// Parameterless, with runtime dependencies (EventBus, IMapQuery, IPlayerQuery) supplied via
/// IGameModule.Configure.
/// </summary>
public sealed class ContactDamageModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000a");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private EventBus _eventBus = null!;
    private IMapQuery _mapQuery = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _mapQuery = context.MapQuery;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<DamageOnContactComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ContactDamageExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        systemManager.Register(new ContactDamageSystem(
            componentManager.GetPackedPool<DamageOnContactComponent>(),
            componentManager.GetPackedPool<ContactDamageExposureComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _eventBus,
            _mapQuery,
            _playerQuery));
    }
}
