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
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

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
        PlayerMovementController playerMovementController)
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
        var inventoryServices = new InventoryServices(itemCatalog, views.InventoryView, views.ShopView, views.CurrencyView, views.ActionStateView, commands.InventoryCommands, commands.ShopCommands, commands.CurrencyCommands, catalogs.GameplayTags);

        uiEngineContext.RegisterFrameworkElementFactories(contextMenuController, cursorTextContent);

        // Supplies the fontService/elementPool/renderService trio every plain registration repeats, so
        // each call site below only has to spell out its own type-specific extras.
        void Register<TElement>(Func<FontService, ElementPoolService, UiRenderService, TElement> factory)
            where TElement : Element
            => elementPool.RegisterFactory(() => factory(uiEngineContext.FontService, elementPool, uiEngineContext.RenderService));

        // MapWindow's dependencies (the map view query, renderers) come from Game and Presentation
        // both, plus the map-specific services built alongside it -- too many type-specific extras
        // for the Register helper above to pull its weight.
        var floatingTextController = new FloatingTextController(ecsContext.EventBus, gameSession.SimulationClock);
        var floatingTextRenderer = new FloatingTextRenderer(floatingTextController, camera, uiEngineContext.FontService, statusEffectDisplays, uiEngineContext.SpriteSheetService);
        var mapRenderer = new MapRenderer(mapView, views.AuraPower, camera, uiEngineContext.RenderService, uiEngineContext.FontService, uiEngineContext.SpriteSheetService);
        var mapGameOverlay = new MapGameOverlay(mapView, mapViewState, camera, actionTargetingController, uiEngineContext.FontService);
        elementPool.RegisterFactory<MapWindow>(() => new MapWindow(
            uiEngineContext.FontService,
            elementPool,
            mapView,
            views.PlayerActionGate,
            mapViewState,
            ecsContext.EventBus,
            uiEngineContext.RenderService,
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


        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService,
            inventoryServices, world, contextMenuController, mapViewState, simulationClock: gameSession.SimulationClock));
        Register<InventoryItemStackCell>((font, elements, render) => new InventoryItemStackCell(font, elements, render, uiEngineContext.SpriteSheetService));
        Register<ShopItemStackCell>((font, elements, render) => new ShopItemStackCell(font, elements, render, uiEngineContext.SpriteSheetService));
        Register<TradeItemStackCell>((font, elements, render) => new TradeItemStackCell(font, elements, render, uiEngineContext.SpriteSheetService));
        Register<EmptyTradeSlotCell>((font, elements, render) => new EmptyTradeSlotCell(font, elements, render));

        elementPool.RegisterFactory<AbilityScoreWindow>(() => new AbilityScoreWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService, views.AbilityScoreView, views.StatModifierView, views.ActionSourceNaming, gameSession.SimulationClock, shellServices.PointerState));
        elementPool.RegisterFactory<HealthWindow>(() => new HealthWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService, views.HealthView, views.StatModifierView, views.ActionStateView, views.EntityBodyParts, statusEffectDisplays, itemCatalog, catalogs.GameplayTags, gameSession.SimulationClock,
            views.ActionSourceNaming, shellServices.TooltipController, shellServices.PointerState));
        Register<AbilityScoreColumnHeader>((font, elements, render) => new AbilityScoreColumnHeader(font, elements, render));
        Register<AbilityScoreModifierRow>((font, elements, render) => new AbilityScoreModifierRow(font, elements, render));

        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService, inventoryServices, world, contextMenuController, mapViewState,
            simulationClock: gameSession.SimulationClock, entityNaming: views.EntityNaming, healthView: views.HealthView));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService, inventoryServices, world, contextMenuController, mapViewState,
            simulationClock: gameSession.SimulationClock, entityNaming: views.EntityNaming));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.RenderService, inventoryServices, world, contextMenuController, mapViewState, simulationClock: gameSession.SimulationClock));
        Register<EntityIconElement>((font, elements, render) => new EntityIconElement(font, elements, render, uiEngineContext.SpriteSheetService,
            mapView));

        Register<InspectionWindow>((font, elements, render) => new InspectionWindow(font, elements, render, mapViewState));
        Register<ItemDetailsWindow>((font, elements, render) => new ItemDetailsWindow(font, elements, render, actionCatalog, catalogs.GameplayTags, views.ActionStateView, world));
        Register<ItemIconElement>((font, elements, render) => new ItemIconElement(font, elements, render, uiEngineContext.SpriteSheetService));
        Register<CurrencyElement>((font, elements, render) => new CurrencyElement(font, elements, render, uiEngineContext.SpriteSheetService));
        Register<TargetShapePreviewElement>((font, elements, render) => new TargetShapePreviewElement(font, elements, render));
    }
}
