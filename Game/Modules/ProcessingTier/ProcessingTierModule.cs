using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.ProcessingTier.Systems;
using Game.World;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// Parameterless (required for runtime discovery) with its one runtime dependency (IPlayerQuery)
/// supplied via IGameModule.Configure instead of the constructor, same shape as MovementModule.
/// No IEntityMoveSync/EventBus needed -- ProcessingTierSystem only reads TransformComponent and
/// writes its own ProcessingTierComponent, never touches map occupancy or publishes events.
/// </summary>
public sealed class ProcessingTierModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000016");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private IPlayerQuery? _playerQuery;
    private ProcessingTierEvents _events = null!;

    public void Configure(GameModuleContext context)
    {
        _playerQuery = context.PlayerQuery;
        _events = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        systemManager.Register(new ProcessingTierSystem(
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _playerQuery,
            _events));
}
