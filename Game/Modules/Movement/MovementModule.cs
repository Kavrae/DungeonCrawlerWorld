using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.World;

namespace Game.Modules.Movement;

/// <summary>
/// Parameterless (required for runtime discovery -- see decision #1/#2 in the modding plan)
/// with its runtime dependencies (IMapQuery, MathUtility, EventBus) supplied via
/// IGameModule.Configure instead of the constructor.
/// </summary>
public sealed class MovementModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000004");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(CoreModule)];

    private IMapQuery _mapQuery = null!;
    private MathUtility _mathUtility = null!;
    private EventBus _eventBus = null!;
    private IEntityMoveSync? _entityMoveSync;
    private FrameEventBuffer<EntityMoved> _movedEntities = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _mathUtility = context.MathUtility;
        _eventBus = context.EventBus;
        _entityMoveSync = context.EntityMoveSync;
        _movedEntities = context.MovedEntities;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) =>
        {
            existing.MovementMode = (MovementMode)System.Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode);
            existing.ActionCooldownFrames = (short)((existing.ActionCooldownFrames + incoming.ActionCooldownFrames) / 2);
            existing.FramesToWait = (short)((existing.FramesToWait + incoming.FramesToWait) / 2);
            existing.NextMapPosition = incoming.NextMapPosition;
            existing.TargetMapPosition = incoming.TargetMapPosition;
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (_entityMoveSync is null)
        {
            throw new InvalidOperationException($"{nameof(MovementModule)} requires {nameof(GameModuleContext)}.{nameof(GameModuleContext.EntityMoveSync)} to be set.");
        }

        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;

        systemManager.Register(new MovementSystem(
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            _mapQuery,
            _mathUtility,
            _eventBus,
            _entityMoveSync,
            _movedEntities,
            _playerQuery,
            deadEntities));

        systemManager.RegisterFrameScoped(_movedEntities);
    }
}