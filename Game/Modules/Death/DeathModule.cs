using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Death.Systems;
using Game.Modules.StatusEffectAura.Components;
using Game.World;
using Game.Modules.Core;
using Game.Modules.StatusEffectAura;

namespace Game.Modules.Death;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (EventBus,
/// IEntityMoveSync) supplied via IGameModule.Configure instead of the constructor -- same shape
/// as MovementModule, the other mandatory IEntityMoveSync consumer.
/// </summary>
public sealed class DeathModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000015");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, StatusEffectAuraModule.ModuleId];

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

        // Rare in practice (player-action-only, seconds to minutes between uses), so initialCapacity is reduced.
        componentManager.RegisterPackedPool<LootedComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 32);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_entityMoveSync is null)
        {
            throw new InvalidOperationException($"{nameof(DeathModule)} requires {nameof(GameModuleContext)}.{nameof(GameModuleContext.EntityMoveSync)} to be set.");
        }

        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();

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
