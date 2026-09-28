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
using Game.Modules.StatModifiers;
using Game.Modules.Death;
using Game.Modules.Race;
using Engine.Modules;

namespace Game.Modules.ContactDamage;

/// <summary>
/// Generic "damage whatever stands on me" hazard support for terrain with a ContactHazard -- Lava
/// is the first (see BuiltInTerrain), but nothing here is lava-specific.
/// Runs after MovementModule so ContactDamageSystem's own Update
/// always runs after MovementSystem's within the same SystemManager.Update() cycle -- required
/// for it to see this frame's moves via the shared FrameEventBuffer&lt;EntityMovedEvent&gt; (see
/// that class's own doc comment on why producer-before-consumer ordering matters).
/// </summary>
public sealed class ContactDamageModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000a");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [MovementModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, RaceModule.ModuleId];

    public IReadOnlyList<Guid> RunsAfter { get; } = [MovementModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<ContactDamageExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, context.Definitions);

        systemManager.Register(new ContactDamageSystem(
            context.Terrain,
            componentManager.GetPackedPool<ContactDamageExposureComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            context.EventBus,
            context.MapQuery,
            context.PlayerQuery,
            context.MovedEntities,
            context.MathUtility,
            context.SimulationClock,
            statModifiers,
            deadEntities,
            bodyParts,
            context.SimulationScope,
            context.FloatingTextFeed));
    }
}
