using Game.Admin;
using Game.Bootstrap;
using Presentation.MapRendering;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Content;
using Presentation.UI.FloatingText;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;
using Presentation.UI.Shops;
using Presentation.UI.Trade;
using UIEngine.Bootstrap;

namespace DungeonCrawlerWorld;

/// <summary>Registers all UI elements to their own factories with individual element pools.</summary>
public static class ElementFactoryRegistry
{
    public static void RegisterAll(
        UiEngineContext uiEngineContext,
        GameSession gameSession,
        AdminTools adminTools,
        ShellServices shellServices,
        ActionTargetingController actionTargetingController,
        PlayerMovementController playerMovementController,
        PlayerItemDrops playerItemDrops)
    {
        var elementPool = uiEngineContext.ElementPoolService;
        var mapViewState = shellServices.MapViewState;
        var camera = shellServices.Camera;
        var cursorTextContent = shellServices.CursorTextContent;
        var contextMenuController = shellServices.ContextMenuController;
        var ecsContext = gameSession.EcsContext;
        var componentManager = ecsContext.ComponentManager;
        var world = gameSession.World;
        var catalogs = gameSession.Catalogs;
        var actionCatalog = catalogs.ActionCatalog;
        var itemCatalog = catalogs.ItemCatalog;
        var statusEffectDisplays = catalogs.StatusEffectDisplays;
        var views = gameSession.Views;
        var mapView = views.MapView;
        var commands = gameSession.Commands;
        var inventoryServices = new InventoryServices(itemCatalog, views.InventoryView, views.ShopView, views.CurrencyView, views.ActionStateView, commands.InventoryCommands, commands.ShopCommands, commands.CurrencyCommands, catalogs.GameplayTags, shellServices.ItemHoverDetails, playerItemDrops, commands.LootCommands);

        uiEngineContext.RegisterFrameworkElementFactories(contextMenuController, cursorTextContent);

        var services = uiEngineContext.ElementServices;
        var sprites = uiEngineContext.SpriteSheetService;
        var floatingTextController = new FloatingTextController(ecsContext.EventBus, gameSession.SimulationClock);
        var floatingTextRenderer = new FloatingTextRenderer(floatingTextController, camera, uiEngineContext.FontService, statusEffectDisplays, uiEngineContext.SpriteSheetService);
        var mapRenderer = new MapRenderer(mapView, views.AuraView, camera, uiEngineContext.RenderService, uiEngineContext.FontService, uiEngineContext.SpriteSheetService);
        var mapGameOverlay = new MapGameOverlay(mapView, mapViewState, camera, actionTargetingController, uiEngineContext.FontService);
        elementPool.RegisterFactory(() => new MapWindow(
            services,
            mapView,
            views.PlayerActionGate,
            mapViewState,
            ecsContext.EventBus,
            camera,
            mapRenderer,
            mapGameOverlay,
            actionTargetingController,
            playerMovementController,
            contextMenuController,
            floatingTextController,
            floatingTextRenderer,
            new AdminContextMenuOptions(adminTools),
            shellServices.PointerState));

        elementPool.RegisterFactory(() => new InventoryManagementWindow(
            services, inventoryServices, world, contextMenuController, mapViewState));
        elementPool.RegisterFactory(() => new InventoryItemStackCell(services, sprites));
        elementPool.RegisterFactory(() => new HotbarSlotElement(services));
        elementPool.RegisterFactory(() => new ShopItemStackCell(services, sprites));
        elementPool.RegisterFactory(() => new TradeItemStackCell(services, sprites));
        elementPool.RegisterFactory(() => new EmptyTradeSlotCell(services));

        elementPool.RegisterFactory(() => new AbilityScoreWindow(
            services, views.AbilityScoreView, views.StatModifierView, views.ActionSourceNaming, gameSession.SimulationClock));
        elementPool.RegisterFactory(() => new HealthWindow(
            services, views.HealthView, views.StatModifierView, views.ActionStateView, views.EntityBodyParts, statusEffectDisplays, itemCatalog, catalogs.GameplayTags, gameSession.SimulationClock,
            views.ActionSourceNaming, shellServices.TooltipController));
        elementPool.RegisterFactory(() => new AbilityScoreColumnHeader(services));
        elementPool.RegisterFactory(() => new AbilityScoreModifierRow(services));

        elementPool.RegisterFactory(() => new SecondaryInventoryWindow(
            services, inventoryServices, world, contextMenuController, mapViewState,
            entityNaming: views.EntityNaming, healthView: views.HealthView, actionSourceNaming: views.ActionSourceNaming));
        elementPool.RegisterFactory(() => new ShopWindow(
            services, inventoryServices, world, contextMenuController, mapViewState,
            entityNaming: views.EntityNaming));
        elementPool.RegisterFactory(() => new TradeWindow(
            services, inventoryServices, world, contextMenuController, mapViewState));
        elementPool.RegisterFactory(() => new EntityIconElement(services, sprites, mapView));

        elementPool.RegisterFactory(() => new InspectionWindow(services, mapViewState));
        elementPool.RegisterFactory(() => new ItemDetailsWindow(services, actionCatalog, catalogs.GameplayTags, views.ActionStateView, world));
        elementPool.RegisterFactory(() => new ItemIconElement(services, sprites));
        elementPool.RegisterFactory(() => new CurrencyElement(services, sprites));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(services));
    }
}
