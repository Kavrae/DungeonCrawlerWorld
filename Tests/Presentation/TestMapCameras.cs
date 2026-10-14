using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Terrain;
using Game.Views;
using Presentation.MapRendering;

namespace Tests.Presentation;

/// <summary>A MapCamera over a real MapViewQuery of world, for tests that need a camera but no map view of their own.</summary>
internal static class TestMapCameras
{
    public static MapCamera Over(Game.World.World world) =>
        new(new MapViewQuery(world, BuiltInTestComponents.RegisterAll(new ComponentManager(100, 50)), new ActionCatalog(), new TerrainRegistry(), new BlueprintRegistry(), new SimulationClock()));
}
