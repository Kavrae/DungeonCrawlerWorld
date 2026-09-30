using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.FloatingText;

namespace Tests.Presentation;

/// <summary>A real MapWindow over a bare world, with everything it needs built headlessly, in an element pool of its own.</summary>
internal static class TestMapWindows
{
    public const int PlayerEntityId = 1;

    public sealed record MapWindowHarness(
        Game.World.World World,
        MapViewState MapViewState,
        MapWindow MapWindow,
        ComponentManager ComponentManager,
        ActionTargetingController ActionTargetingController);

    /// <param name="playerPosition">Where a PlayerControlled player (PlayerEntityId) stands, with every hotkey slot unlocked; null for no player.</param>
    public static MapWindowHarness Create(int mapSizeX, int mapSizeY, int mapSizeZ, Vector3Int? playerPosition, ActionCatalog? actionCatalog = null, ItemCatalog? itemCatalog = null)
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(mapSizeX, mapSizeY, mapSizeZ)));
        var mapViewState = new MapViewState();
        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService, new LabelRenderer());

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(100, 50));

        if (playerPosition is { } position)
        {
            TestTransforms.Set(componentManager, PlayerEntityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            componentManager.Merge(PlayerEntityId, new MovementComponent(MovementMode.PlayerControlled, null, null));
            // Fully unlocked -- these tests are about arm/target/confirm behavior, not the Expansion lock itself, so default to every slot being usable rather than incidentally locking out whichever slot a given test happens to bind to.
            componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 20));
            world.PlayerEntityId = PlayerEntityId;
        }

        var resolvedActionCatalog = actionCatalog ?? new ActionCatalog();
        var resolvedItemCatalog = itemCatalog ?? new ItemCatalog();
        var camera = new MapCamera(world);
        var eventBus = new EventBus();
        var playerCommands = new PlayerCommands(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            new SimulationClock(),
            eventBus);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            new UiLayerStack(),
            resolvedActionCatalog,
            resolvedItemCatalog,
            new TransformView(componentManager),
            new HotkeyBindingView(componentManager),
            new InventoryView(componentManager, resolvedItemCatalog),
            new ActionStateView(componentManager, localTierRoster: null),
            new AbilityScoreView(componentManager),
            playerCommands, simulationClock: new SimulationClock());
        var playerMovement = new PlayerMovementController(playerCommands);

        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var terrain = new Game.Terrain.TerrainRegistry();
        var mapView = new MapViewQuery(world, componentManager, resolvedActionCatalog, terrain, creatures: new BlueprintRegistry(), new SimulationClock());
        var playerActionGate = new PlayerActionGate(componentManager.GetPackedPool<ActionLockComponent>(), world, new SimulationClock());
        var tintGrid = new MapTintGrid(componentManager, world, terrain, eventBus);

        var floatingTextController = new FloatingTextController(eventBus, new SimulationClock());
        var floatingTextRenderer = new FloatingTextRenderer(floatingTextController, camera, fontService, new Game.Modules.StatusEffects.StatusEffectDisplayRegistry(), new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new LabelRenderer());

        windowService.RegisterFactory<MapWindow>(() => new MapWindow(
            fontService, windowService, mapView, playerActionGate, mapViewState, tintGrid, eventBus, new TileRenderer(), new LabelRenderer(),
            new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), camera, actionTargeting, playerMovement, contextMenuController, floatingTextController, floatingTextRenderer, new AdminContextMenuOptions(TestAdminTools.Create())));

        var mapWindow = windowService.CreateElement<MapWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(1256, 776), DisplayMode = ElementDisplayMode.Fixed },
        });
        mapWindow.Initialize();

        return new MapWindowHarness(world, mapViewState, mapWindow, componentManager, actionTargeting);
    }
}
