using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;

namespace Tests;

/// <summary>Worlds built directly by tests, for a test that needs a World without building the modules.</summary>
internal static class TestWorlds
{
    /// <summary>A World over map, with fresh occupancy pools, event bus, key table and terrain registry for anything not given.</summary>
    public static Game.World.World Create(
        Map map,
        MultiComponentPool<NonBlockingComponent>? nonBlockingComponents = null,
        MultiComponentPool<ForceBlockingComponent>? forceBlockingComponents = null,
        EventBus? eventBus = null,
        EntityKeys? entityKeys = null,
        TerrainRegistry? terrain = null,
        int playerEntityId = -1) =>
        new(
            map,
            nonBlockingComponents ?? new MultiComponentPool<NonBlockingComponent>(16, 16),
            forceBlockingComponents ?? new MultiComponentPool<ForceBlockingComponent>(16, 16),
            eventBus ?? new EventBus(),
            entityKeys ?? new EntityKeys(),
            terrain ?? new TerrainRegistry())
        {
            PlayerEntityId = playerEntityId,
        };

    /// <summary>A World over map whose occupancy pools are componentManager's.</summary>
    public static Game.World.World Over(Map map, ComponentManager componentManager, EventBus? eventBus = null, EntityKeys? entityKeys = null, TerrainRegistry? terrain = null, int playerEntityId = -1) =>
        Create(map, componentManager.GetMultiPool<NonBlockingComponent>(), componentManager.GetMultiPool<ForceBlockingComponent>(), eventBus, entityKeys, terrain, playerEntityId);
}
