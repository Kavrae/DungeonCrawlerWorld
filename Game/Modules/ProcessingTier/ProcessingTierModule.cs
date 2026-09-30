using Engine.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.ProcessingTier.Systems;

namespace Game.Modules.ProcessingTier;

/// <summary>Registers every entity's processing tier and the system that keeps it current.</summary>
/// <remarks>
/// Runs after MovementModule: ProcessingTierSystem drains the shared FrameEventBuffer&lt;EntityMovedEvent&gt;
/// that MovementSystem records into, and the buffer is cleared at the end of every frame -- so
/// ProcessingTierSystem has to run after MovementSystem within the frame or it sees nothing. That is order
/// only: Movement's pools aren't read, so it is not required.
/// </remarks>
public sealed class ProcessingTierModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001c");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId];

    public IReadOnlyList<Guid> RunsAfter { get; } = [MovementModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration) =>
        registration.ComponentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var transforms = registration.ComponentManager.GetDirectPool<TransformComponent>();

        registration.SystemManager.Register(new ProcessingTierSystem(transforms, context.MapQuery, context.MovedEntities, context.ProcessingTierResolver, context.PlayerQuery));
    }
}
