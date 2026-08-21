using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.Modules.StatusEffectAura.Components;
using Game.World;

namespace Game.Modules.Death;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (EventBus,
/// IEntityMoveSync) supplied via IGameModule.Configure instead of the constructor -- same shape
/// as MovementModule, the other mandatory IEntityMoveSync consumer.
/// </summary>
public sealed class DeathModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000015");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private EventBus _eventBus = null!;
    private IMapQuery _mapQuery = null!;
    private IEntityMoveSync? _entityMoveSync;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _mapQuery = context.MapQuery;
        _entityMoveSync = context.EntityMoveSync;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<CorpseLootedComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_entityMoveSync is null)
        {
            throw new InvalidOperationException($"{nameof(DeathModule)} requires {nameof(GameModuleContext)}.{nameof(GameModuleContext.EntityMoveSync)} to be set.");
        }

        // Soft, IsRegistered-guarded dependency on StatusEffectAuraModule's own component --
        // DeathModule doesn't otherwise need anything from that module, so this stays optional
        // rather than a declared Dependencies entry (contrast InventoryModule's own hard
        // dependency on ActionsModule's PotionCooldownComponent, which InventoryModule cannot
        // function without at all).
        var auraSources = componentManager.IsRegistered<StatusEffectAuraSourceComponent>()
            ? componentManager.GetMultiPool<StatusEffectAuraSourceComponent>()
            : null;

        systemManager.Register(new DeathSystem(
            componentManager.GetPackedPool<DeadComponent>(),
            componentManager.GetMultiPool<NonBlockingComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            _entityMoveSync,
            _mapQuery,
            _eventBus,
            auraSources));
    }
}
