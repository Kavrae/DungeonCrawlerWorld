using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;
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

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(CoreModule), typeof(EnergyModule)];

    private IMapQuery _mapQuery = null!;
    private MathUtility _mathUtility = null!;
    private EventBus _eventBus = null!;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _mathUtility = context.MathUtility;
        _eventBus = context.EventBus;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) =>
        {
            existing.MovementMode = (MovementMode)System.Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode);
            existing.EnergyToMove = (short)((existing.EnergyToMove + incoming.EnergyToMove) / 2);
            existing.FramesToWait = (short)((existing.FramesToWait + incoming.FramesToWait) / 2);
            existing.NextMapPosition = incoming.NextMapPosition;
            existing.TargetMapPosition = incoming.TargetMapPosition;
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new MovementSystem(
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<EnergyComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            _mapQuery,
            _mathUtility,
            _eventBus));
    }
}