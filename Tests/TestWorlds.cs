using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests;

/// <summary>Worlds built directly by tests, wired the way GameBootstrapper.Build wires the real one.</summary>
internal static class TestWorlds
{
    /// <summary>A World over map with empty occupancy pools of its own.</summary>
    public static Game.World.World Create(Map map) =>
        WithOccupancy(new Game.World.World(map), new MultiComponentPool<NonBlockingComponent>(16, 16), new MultiComponentPool<ForceBlockingComponent>(16, 16));

    /// <summary>Wires world's occupancy pools to componentManager's, returning world.</summary>
    public static Game.World.World WireOccupancy(Game.World.World world, ComponentManager componentManager) =>
        WithOccupancy(world, componentManager.GetMultiPool<NonBlockingComponent>(), componentManager.GetMultiPool<ForceBlockingComponent>());

    private static Game.World.World WithOccupancy(Game.World.World world, MultiComponentPool<NonBlockingComponent> nonBlocking, MultiComponentPool<ForceBlockingComponent> forceBlocking)
    {
        world.NonBlockingComponents = nonBlocking;
        world.ForceBlockingComponents = forceBlocking;
        return world;
    }
}
