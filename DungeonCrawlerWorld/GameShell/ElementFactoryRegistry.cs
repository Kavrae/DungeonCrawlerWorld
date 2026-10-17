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

        // Supplies the fontService/elementPool/labelRenderer trio every plain registration repeats, so
        // each call site below only has to spell out its own type-specific extras.
        void Register<TElement>(Func<FontService, ElementPoolService, LabelRenderer, TElement> factory)
            where TElement : Element
            => elementPool.RegisterFactory(() => factory(uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer));

        Register<Window>((font, elements, glyph) => new Window(font, elements, glyph));
        Register<Button>((font, elements, glyph) => new Button(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<ContextMenu>((font, elements, glyph) => new ContextMenu(font, elements, glyph));
        Register<TextWindow>((font, elements, glyph) => new TextWindow(font, elements, glyph));
        Register<TextBox>((font, elements, glyph) => new TextBox(font, elements, glyph, cursorTextContent, contextMenuController));

        // MapWindow's dependencies (the map view query, renderers) come from Game and Presentation
        // both, plus the map-specific services built alongside it -- too many type-specific extras
        // for the Register helper above to pull its weight.
        var floatingTextController = new FloatingTextController(ecsContext.EventBus, gameSession.SimulationClock);
        var floatingTextRenderer = new FloatingTextRenderer(floatingTextController, camera, uiEngineContext.FontService, statusEffectDisplays, uiEngineContext.SpriteSheetService, uiEngineContext.LabelRenderer);
        var mapRenderer = new MapRenderer(mapView, views.AuraPower, camera, elementPool, uiEngineContext.FontService, uiEngineContext.SpriteSheetService, uiEngineContext.LabelRenderer);
        var mapGameOverlay = new MapGameOverlay(mapView, mapViewState, camera, actionTargetingController, uiEngineContext.FontService, uiEngineContext.LabelRenderer);
        elementPool.RegisterFactory<MapWindow>(() => new MapWindow(
            uiEngineContext.FontService,
            elementPool,
            mapView,
            views.PlayerActionGate,
            mapViewState,
            ecsContext.EventBus,
            uiEngineContext.LabelRenderer,
            camera,
            mapRenderer,
            mapGameOverlay,
            actionTargetingController,
            playerMovementController,
            contextMenuController,
            floatingTextController,
            floatingTextRenderer,
            new AdminContextMenuOptions(adminTools)));

        Register<Folder>((font, elements, glyph) => new Folder(font, elements, glyph, uiEngineContext.SpriteSheetService));

        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer,
            inventoryServices, world, contextMenuController, mapViewState, simulationClock: gameSession.SimulationClock));
        Register<InventoryItemStackCell>((font, elements, glyph) => new InventoryItemStackCell(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<ShopItemStackCell>((font, elements, glyph) => new ShopItemStackCell(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<TradeItemStackCell>((font, elements, glyph) => new TradeItemStackCell(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<EmptyTradeSlotCell>((font, elements, glyph) => new EmptyTradeSlotCell(font, elements, glyph));
        Register<GridControl>((font, elements, glyph) => new GridControl(font, elements, glyph));
        Register<Toggle>((font, elements, glyph) => new Toggle(font, elements, glyph));

        elementPool.RegisterFactory<AbilityScoreWindow>(() => new AbilityScoreWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer, views.AbilityScoreView, views.StatModifierView, views.ActionSourceNaming, gameSession.SimulationClock, shellServices.PointerState));
        elementPool.RegisterFactory<HealthWindow>(() => new HealthWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer, views.HealthView, views.StatModifierView, views.ActionStateView, views.EntityBodyParts, statusEffectDisplays, itemCatalog, catalogs.GameplayTags, gameSession.SimulationClock,
            views.ActionSourceNaming, shellServices.TooltipController, shellServices.PointerState));
        Register<AbilityScoreColumnHeader>((font, elements, glyph) => new AbilityScoreColumnHeader(font, elements, glyph));
        Register<AbilityScoreModifierRow>((font, elements, glyph) => new AbilityScoreModifierRow(font, elements, glyph));
        Register<SeparatorBar>((font, elements, glyph) => new SeparatorBar(font, elements, glyph));
        Register<TextDivider>((font, elements, glyph) => new TextDivider(font, elements, glyph));
        Register<Tooltip>((font, elements, glyph) => new Tooltip(font, elements, glyph));

        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer, inventoryServices, world, contextMenuController, mapViewState,
            simulationClock: gameSession.SimulationClock, entityNaming: views.EntityNaming, healthView: views.HealthView));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer, inventoryServices, world, contextMenuController, mapViewState,
            simulationClock: gameSession.SimulationClock, entityNaming: views.EntityNaming));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(
            uiEngineContext.FontService, elementPool, uiEngineContext.LabelRenderer, inventoryServices, world, contextMenuController, mapViewState, simulationClock: gameSession.SimulationClock));
        Register<EntityIconElement>((font, elements, glyph) => new EntityIconElement(
            font, elements, glyph, uiEngineContext.SpriteSheetService,
            mapView));

        Register<InspectionWindow>((font, elements, glyph) => new InspectionWindow(font, elements, glyph, mapViewState));
        Register<ItemDetailsWindow>((font, elements, glyph) => new ItemDetailsWindow(font, elements, glyph, actionCatalog, catalogs.GameplayTags, views.ActionStateView, world));
        Register<ItemIconElement>((font, elements, glyph) => new ItemIconElement(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<CurrencyElement>((font, elements, glyph) => new CurrencyElement(font, elements, glyph, uiEngineContext.SpriteSheetService));
        Register<TargetShapePreviewElement>((font, elements, glyph) => new TargetShapePreviewElement(font, elements, glyph));
        Register<FractionBarElement>((font, elements, glyph) => new FractionBarElement(font, elements, glyph));
    }
}
