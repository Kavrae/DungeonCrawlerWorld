using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;

namespace Mods.TestFixtures;

/// <summary>
/// Deliberately throws during RegisterComponents -- a fixture for exercising
/// GameBootstrapper's dry-run failure isolation: a mod like this must be excluded and
/// reported without preventing the rest of the world (built-ins and other mods) from
/// building. Kept in a separate assembly from Mods.ExampleMod (the plan's shippable
/// trivial-mod fixture) so each can be dropped into Mods/ independently for a full game run
/// without one adversarial fixture's effects bleeding into another's verification.
/// </summary>
public sealed class ThrowingModule : IModule
{
    public Guid Id { get; } = new("e6f2a017-4b3d-4a1e-9c72-000000000002");

    public void RegisterComponents(ComponentManager componentManager) =>
        throw new InvalidOperationException("Intentional failure for GameBootstrapper dry-run testing.");

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
}