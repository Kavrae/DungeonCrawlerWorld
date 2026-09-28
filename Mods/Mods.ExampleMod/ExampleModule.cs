using Engine.Modules;
using Game.Modules;

namespace Mods.ExampleMod;

/// <summary>
/// A trivial mod, deliberately doing nothing beyond existing -- proves the discovery/load/
/// registration pipeline (ModuleLoader -> GameBootstrapper) works end to end against a real
/// compiled assembly dropped in Mods/, not just an in-process test double. Its Id is fresh
/// (no built-in shares it), so it's added alongside the built-ins rather than replacing one.
/// </summary>
public sealed class ExampleModule : IGameModule
{
    public Guid Id { get; } = new("e6f2a017-4b3d-4a1e-9c72-000000000001");

    public void RegisterComponents(ComponentRegistration registration) { }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration) { }
}
