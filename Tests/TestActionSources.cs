using Engine.ECS.Entities;
using Game.Modules.Auras;
using Game.Terrain;
using Game.Views;

namespace Tests;

/// <summary>Source naming for a test that doesn't build content of its own.</summary>
internal static class TestActionSources
{
    public static ActionSourceNaming Naming(TerrainRegistry? terrain = null, AuraCatalog? auras = null, EntityKeys? entityKeys = null) =>
        new(terrain ?? new TerrainRegistry(), auras ?? new AuraCatalog(), entityKeys ?? new EntityKeys());
}
