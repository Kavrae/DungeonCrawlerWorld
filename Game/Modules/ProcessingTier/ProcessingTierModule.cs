using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.ProcessingTier.Systems;
using Game.World;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies supplied via
/// IGameModule.Configure instead of the constructor, same shape as MovementModule.
/// </summary>
/// <remarks>
/// Depends on MovementModule for <b>ordering</b>, not just for MovementComponent: ProcessingTierSystem
/// drains the shared FrameEventBuffer&lt;EntityMovedEvent&gt; that MovementSystem records into, and the
/// buffer is cleared at the end of every frame -- so ProcessingTierSystem has to run after
/// MovementSystem within the frame or it sees nothing. Systems run in module order, and this makes
/// that order a declared dependency rather than a coincidence of the built-in module list.
/// </remarks>
public sealed class ProcessingTierModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000016");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(MovementModule)];

    private IPlayerQuery? _playerQuery;
    private IMapQuery _mapQuery = null!;
    private FrameEventBuffer<EntityMovedEvent> _movedEntities = null!;
    private ProcessingTierEvents _events = null!;
    private LocalTierRoster _localTierRoster = null!;
    private ProcessingTierResolver _resolver = null!;

    public void Configure(GameModuleContext context)
    {
        _playerQuery = context.PlayerQuery;
        _mapQuery = context.MapQuery;
        _movedEntities = context.MovedEntities;
        _events = context.ProcessingTierEvents;
        _localTierRoster = context.LocalTierRoster;
        _resolver = context.ProcessingTierResolver;
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var tiers = componentManager.GetDirectPool<ProcessingTierComponent>();
        var transforms = componentManager.GetDirectPool<TransformComponent>();

        _resolver.Wire(tiers, transforms, _events);

        // The roster stays scoped to movers -- see LocalTierRoster.Wire -- even though tiering now
        // covers every positioned entity. It exists to be the small side of "Local AND pending
        // something", and terrain within the Local radius would make it roughly fifty times larger.
        _localTierRoster.Wire(componentManager.GetPackedPool<MovementComponent>(), tiers, _events);

        systemManager.Register(new ProcessingTierSystem(transforms, _mapQuery, _movedEntities, _resolver, _playerQuery));
    }
}
