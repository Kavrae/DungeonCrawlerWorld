using Engine.Modules;
using Game.Effects;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.TerrainContacts.Components;
using Game.Modules.TerrainContacts.Systems;

namespace Game.Modules.TerrainContacts;

/// <summary>Terrain contact: what a terrain does to whatever stands on it (TerrainDefinition.Contact).</summary>
/// <remarks>
/// Knows no particular effect -- a contact is a list of Effect, the same lists an action or item holds.
/// Runs after MovementModule so TerrainContactSystem's Update follows MovementSystem's within a
/// frame, which it needs to see that frame's moves in the shared FrameEventBuffer&lt;EntityMovedEvent&gt;.
/// </remarks>
public sealed class TerrainContactsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000a");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, MovementModule.ModuleId, .. EffectServices.RequiredModuleIds];

    public IReadOnlyList<Guid> RunsAfter { get; } = [MovementModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<TerrainContactExposureComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new TerrainContactSystem(
            context.Terrain,
            componentManager.GetPackedPool<TerrainContactExposureComponent>(),
            context.EffectServices,
            context.MapQuery,
            componentManager.GetDirectPool<TransformComponent>(),
            context.MovedEntities,
            context.SimulationClock,
            context.SimulationScope));
    }
}
