using Engine.ECS.Context;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.Views;
using Game.World;
using Presentation.Bootstrap;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;
using Presentation.UI.Shops;
using Presentation.UI.Trade;

namespace DungeonCrawlerWorld;

/// <summary>Registers all UI elements to their own factories with individual element pools.</summary>
public static class ElementFactoryRegistry
{
    public static void RegisterAll(
        PresentationContext presentationContext,
        EcsContext ecsContext,
        ActionCatalog actionCatalog,
        ItemCatalog itemCatalog,
        StatusEffectDisplayRegistry statusEffectDisplays,
        World world,
        TerrainRegistry terrain,
        IMapViewQuery mapView,
        MapViewState mapViewState,
        MapCamera camera,
        ActionTargetingController actionTargetingController,
        PlayerMovementController playerMovementController,
        CursorTextContent cursorTextContent,
        ContextMenuController contextMenuController)
    {
        var elementPool = presentationContext.ElementPoolService;
        var componentManager = ecsContext.ComponentManager;

        // Supplies the fontService/elementPool/labelRenderer trio every plain registration repeats, so
        // each call site below only has to spell out its own type-specific extras.
        void Register<TElement>(Func<FontService, ElementPoolService, LabelRenderer, TElement> factory)
            where TElement : Element
            => elementPool.RegisterFactory(() => factory(presentationContext.FontService, elementPool, presentationContext.LabelRenderer));

        Register<Window>((font, elements, glyph) => new Window(font, elements, glyph));
        Register<Button>((font, elements, glyph) => new Button(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<ContextMenu>((font, elements, glyph) => new ContextMenu(font, elements, glyph));
        Register<TextWindow>((font, elements, glyph) => new TextWindow(font, elements, glyph));
        Register<TextBox>((font, elements, glyph) => new TextBox(font, elements, glyph, cursorTextContent, contextMenuController));

        // MapWindow's dependencies (the map view query, renderers) come from Game and Presentation
        // both, plus the map-specific services built alongside it -- too many type-specific extras
        // for the Register helper above to pull its weight.
        var playerActionGate = new PlayerActionGate(componentManager.GetPackedPool<ActionLockComponent>(), world, ecsContext.SystemManager.Clock);
        var mapTintGrid = new MapTintGrid(componentManager, world, terrain, ecsContext.EventBus);
        elementPool.RegisterFactory<MapWindow>(() => new MapWindow(
            presentationContext.FontService,
            elementPool,
            mapView,
            playerActionGate,
            mapViewState,
            mapTintGrid,
            ecsContext.EventBus,
            presentationContext.TileRenderer,
            presentationContext.LabelRenderer,
            presentationContext.SpriteSheetService,
            presentationContext.SpriteRenderer,
            camera,
            actionTargetingController,
            playerMovementController,
            contextMenuController));

        Register<Folder>((font, elements, glyph) => new Folder(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));

        elementPool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer,
            componentManager, itemCatalog, world, contextMenuController, mapViewState, ecsContext.EventBus, simulationClock: ecsContext.SystemManager.Clock));
        Register<InventoryItemStackCell>((font, elements, glyph) => new InventoryItemStackCell(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<ShopItemStackCell>((font, elements, glyph) => new ShopItemStackCell(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<TradeItemStackCell>((font, elements, glyph) => new TradeItemStackCell(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<EmptyTradeSlotCell>((font, elements, glyph) => new EmptyTradeSlotCell(font, elements, glyph));
        Register<GridControl>((font, elements, glyph) => new GridControl(font, elements, glyph));
        Register<Toggle>((font, elements, glyph) => new Toggle(font, elements, glyph));

        elementPool.RegisterFactory<AbilityScoreWindow>(() => new AbilityScoreWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, componentManager, ecsContext.SystemManager.Clock));
        elementPool.RegisterFactory<HealthWindow>(() => new HealthWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, componentManager, statusEffectDisplays, itemCatalog, ecsContext.SystemManager.Clock));
        Register<AbilityScoreColumnHeader>((font, elements, glyph) => new AbilityScoreColumnHeader(font, elements, glyph));
        Register<AbilityScoreModifierRow>((font, elements, glyph) => new AbilityScoreModifierRow(font, elements, glyph));
        Register<SeparatorBar>((font, elements, glyph) => new SeparatorBar(font, elements, glyph));
        Register<TextDivider>((font, elements, glyph) => new TextDivider(font, elements, glyph));
        Register<Tooltip>((font, elements, glyph) => new Tooltip(font, elements, glyph));

        elementPool.RegisterFactory<SecondaryInventoryWindow>(() => new SecondaryInventoryWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, componentManager,
            presentationContext.SpriteSheetService, presentationContext.SpriteRenderer, itemCatalog, world, contextMenuController, mapViewState,
            simulationClock: ecsContext.SystemManager.Clock));
        elementPool.RegisterFactory<ShopWindow>(() => new ShopWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, componentManager,
            presentationContext.SpriteSheetService, presentationContext.SpriteRenderer, itemCatalog, world, contextMenuController, mapViewState,
            simulationClock: ecsContext.SystemManager.Clock));
        elementPool.RegisterFactory<TradeWindow>(() => new TradeWindow(
            presentationContext.FontService, elementPool, presentationContext.LabelRenderer, componentManager,
            itemCatalog, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer, world, contextMenuController, mapViewState,
            ecsContext.EventBus, simulationClock: ecsContext.SystemManager.Clock));
        Register<EntityIconElement>((font, elements, glyph) => new EntityIconElement(
            font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer,
            componentManager.GetDirectPool<SpriteComponent>(), componentManager.GetDirectPool<GlyphComponent>()));

        Register<InspectionWindow>((font, elements, glyph) => new InspectionWindow(font, elements, glyph, mapViewState));
        Register<ItemDetailsWindow>((font, elements, glyph) => new ItemDetailsWindow(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer, actionCatalog));
        Register<ItemIconElement>((font, elements, glyph) => new ItemIconElement(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<CurrencyElement>((font, elements, glyph) => new CurrencyElement(font, elements, glyph, presentationContext.SpriteSheetService, presentationContext.SpriteRenderer));
        Register<TargetShapePreviewElement>((font, elements, glyph) => new TargetShapePreviewElement(font, elements, glyph));
        Register<FractionBarElement>((font, elements, glyph) => new FractionBarElement(font, elements, glyph));
    }
}
