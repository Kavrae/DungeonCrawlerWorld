using Engine.Content;
using Engine.Modules;
using Game.Modules;
using Game.Terrain;

namespace Game.Areas;

/// <summary>Registers the area codecs, and the master manifest's areas, palettes and fixed placements in GameModuleContext.Areas, in file order with each disabled record's replacement as its redirect.</summary>
/// <remarks>No components or systems: areas are stamped by generation, and checked after every Configure (AreaContentValidation).</remarks>
public sealed class AreasModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000030");

    public Guid Id => ModuleId;

    /// <summary>Terrain registers the floors and walls areas name by key.</summary>
    public IReadOnlyList<Guid> Requires { get; } = [TerrainModule.ModuleId];

    public void DeclareContentTypes(ContentTypeDeclarations contentTypes) => AreaContentCodecs.Register(contentTypes);

    public void Configure(GameModuleContext context) => context.Areas.RegisterAll(context.Content);

    public void RegisterComponents(ComponentRegistration registration)
    {
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
    }
}
