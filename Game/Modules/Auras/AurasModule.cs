using Engine.Modules;
using Game.Effects;
using Game.Modules.Auras.Components;
using Game.Modules.Auras.Systems;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.Auras;

/// <summary>Auras: sources that radiate one, the grid of where each reaches, and the once-a-second effect on whatever stands inside.</summary>
/// <remarks>
/// Knows no particular aura and registers none: a definition lives with whatever radiates it (a
/// terrain, a blueprint, an effect entry) and the build registers the ones its content names
/// (AuraContentRegistration). Runs after
/// MovementModule so AuraSystem's Update follows MovementSystem's within a frame, which it needs
/// to see that frame's moves in the shared FrameEventBuffer&lt;EntityMovedEvent&gt;.
/// </remarks>
public sealed class AurasModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000b");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, ProcessingTierModule.ModuleId, .. EffectServices.RequiredModuleIds.Where(static moduleId => moduleId != ModuleId)];

    public IReadOnlyList<Guid> RunsAfter { get; } = [MovementModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<AuraSourceComponent>();
        componentManager.RegisterMultiPool<AuraExposureComponent>();
        componentManager.RegisterPackedPool<AuraSourceExpiryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new AuraSystem(
            componentManager.GetMultiPool<AuraExposureComponent>(),
            componentManager.GetMultiPool<AuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            context.MapQuery,
            context.EventBus,
            context.Auras,
            context.MovedEntities,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.SimulationClock,
            context.AuraField,
            context.EffectServices,
            context.SimulationScope));

        systemManager.Register(new AuraSourceExpirySystem(
            componentManager.GetPackedPool<AuraSourceExpiryComponent>(),
            componentManager.GetMultiPool<AuraSourceComponent>(),
            context.EventBus,
            context.SimulationScope));
    }
}
