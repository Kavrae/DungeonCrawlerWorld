using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Game.Blueprints;

namespace Game.Modules.ContactDamage;

/// <summary>
/// Generic "damage whatever stands on me" hazard support for terrain with a ContactHazard -- Lava
/// is the first (see BuiltInTerrain), but nothing here is lava-specific.
/// Parameterless, with runtime dependencies (EventBus, IMapQuery, IPlayerQuery) supplied via
/// IGameModule.Configure. Depends on MovementModule so ContactDamageSystem's own Update
/// always runs after MovementSystem's within the same SystemManager.Update() cycle -- required
/// for it to see this frame's moves via the shared FrameEventBuffer&lt;EntityMovedEvent&gt; (see
/// that class's own doc comment on why producer-before-consumer ordering matters).
/// </summary>
public sealed class ContactDamageModule : IGameModule
{
    private BlueprintRegistry _creatures = null!;

    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000a");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(MovementModule)];

    private EventBus _eventBus = null!;
    private IMapQuery _mapQuery = null!;
    private IPlayerQuery? _playerQuery;
    private FrameEventBuffer<EntityMovedEvent> _movedEntities = null!;
    private MathUtility _mathUtility = null!;
    private SimulationClock _simulationClock = null!;
    private SimulationScope _simulationScope = null!;
    private Terrain.TerrainRegistry _terrain = null!;

    public void Configure(GameModuleContext context)
    {
        _creatures = context.Definitions;
        _eventBus = context.EventBus;
        _terrain = context.Terrain;
        _simulationClock = context.SimulationClock;
        _simulationScope = context.SimulationScope;
        _mapQuery = context.MapQuery;
        _playerQuery = context.PlayerQuery;
        _movedEntities = context.MovedEntities;
        _mathUtility = context.MathUtility;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<ContactDamageExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<SimpleHealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var bodyParts = componentManager.IsRegistered<BodyPartStateComponent>()
            ? EntityBodyParts.For(componentManager, _creatures)
            : null;

        systemManager.Register(new ContactDamageSystem(
            _terrain,
            componentManager.GetPackedPool<ContactDamageExposureComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _eventBus,
            _mapQuery,
            _playerQuery,
            _movedEntities,
            _mathUtility,
            _simulationClock,
            statModifiers,
            deadEntities,
            bodyParts,
            _simulationScope));
    }
}
