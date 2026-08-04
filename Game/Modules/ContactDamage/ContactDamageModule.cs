using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.ContactDamage;

/// <summary>
/// Generic "damage whatever stands on me" hazard support -- Lava is the first user
/// (DamagePerTick: 10, TickIntervalFrames: 60), but nothing here is lava-specific.
/// Parameterless, with runtime dependencies (EventBus, IMapQuery, IPlayerQuery) supplied via
/// IGameModule.Configure. Depends on MovementModule so ContactDamageSystem's own Update
/// always runs after MovementSystem's within the same SystemManager.Update() cycle -- required
/// for it to see this frame's moves via the shared FrameEventBuffer&lt;EntityMoved&gt; (see
/// that class's own doc comment on why producer-before-consumer ordering matters).
/// </summary>
public sealed class ContactDamageModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000a");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(MovementModule)];

    private EventBus _eventBus = null!;
    private IMapQuery _mapQuery = null!;
    private IPlayerQuery? _playerQuery;
    private FrameEventBuffer<EntityMoved> _movedEntities = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _mapQuery = context.MapQuery;
        _playerQuery = context.PlayerQuery;
        _movedEntities = context.MovedEntities;
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

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;

        systemManager.Register(new ContactDamageSystem(
            componentManager.GetPackedPool<DamageOnContactComponent>(),
            componentManager.GetPackedPool<ContactDamageExposureComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _eventBus,
            _mapQuery,
            _playerQuery,
            _movedEntities,
            statModifiers,
            deadEntities));
    }
}
