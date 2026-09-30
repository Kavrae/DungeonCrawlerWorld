using Engine.Modules;
using Game.Modules;

namespace Game.Terrain;

/// <summary>Registers the built-in terrain definitions. Terrain has no components or systems of its own; its behaviour lives in the systems that read definitions (contact damage, auras).</summary>
public sealed class TerrainModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000020");

    public Guid Id => ModuleId;

    public void Configure(GameModuleContext context) => BuiltInTerrain.RegisterAll(context.Terrain);

    public void RegisterComponents(ComponentRegistration registration)
    {
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
    }
}
