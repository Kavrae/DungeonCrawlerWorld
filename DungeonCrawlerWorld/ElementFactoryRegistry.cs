using Engine.ECS.Context;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Presentation.Bootstrap;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;

namespace DungeonCrawlerWorld;

/// <summary>Registers all UI elements to their own factories with individual element pools.</summary>
public static class ElementFactoryRegistry
{
    public static void RegisterAll(
        PresentationContext presentation,
        EcsContext ecsContext,
        ActionCatalog actionCatalog,
        ItemCatalog itemCatalog,
        World world,
        MapViewState mapViewState,
        MapCamera camera,
        ActionTargetingController actionTargeting,
        PlayerMovementController playerMovement,
        CursorTextContent cursorTextContent,
        ContextMenuController contextMenuController)
    {
        var pool = presentation.ElementPoolService;
        var componentManager = ecsContext.ComponentManager;

        // Supplies the fontService/pool/glyphRenderer trio every plain registration repeats, so
        // each call site below only has to spell out its own type-specific extras.
        void Register<TElement>(Func<FontService, ElementPoolService, GlyphRenderer, TElement> factory)
            where TElement : Element
            => pool.RegisterFactory(() => factory(presentation.FontService, pool, presentation.GlyphRenderer));

        Register<Window>((font, elements, glyph) => new Window(font, elements, glyph));
        Register<Button>((font, elements, glyph) => new Button(font, elements, glyph));
        Register<ContextMenu>((font, elements, glyph) => new ContextMenu(font, elements, glyph));
        Register<TextWindow>((font, elements, glyph) => new TextWindow(font, elements, glyph));
        Register<TextBox>((font, elements, glyph) => new TextBox(font, elements, glyph, cursorTextContent, contextMenuController));

        // MapWindow's dependencies (World/ComponentManager/renderers) come from Engine/Game and
        // Presentation both, plus the map-specific services built alongside it -- too many
        // type-specific extras for the Register helper above to pull its weight.
        pool.RegisterFactory<MapWindow>(() => new MapWindow(
            presentation.FontService,
            pool,
            world,
            mapViewState,
            componentManager,
            ecsContext.EventBus,
            actionCatalog,
            itemCatalog,
            presentation.TileRenderer,
            presentation.GlyphRenderer,
            presentation.SpriteSheetService,
            presentation.SpriteRenderer,
            camera,
            actionTargeting,
            playerMovement,
            contextMenuController,
            componentManager.GetPackedPool<ActionLockComponent>()));

        Register<Folder>((font, elements, glyph) => new Folder(font, elements, glyph, presentation.SpriteSheetService, presentation.SpriteRenderer));

        pool.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            presentation.FontService, pool, presentation.GlyphRenderer, presentation.SpriteSheetService, presentation.SpriteRenderer,
            componentManager, itemCatalog, world, contextMenuController, mapViewState));
        Register<InventoryItemStackCell>((font, elements, glyph) => new InventoryItemStackCell(font, elements, glyph, presentation.SpriteSheetService, presentation.SpriteRenderer));
        Register<GridControl>((font, elements, glyph) => new GridControl(font, elements, glyph));
        Register<Toggle>((font, elements, glyph) => new Toggle(font, elements, glyph));

        pool.RegisterFactory<AbilityScoreWindow>(() => new AbilityScoreWindow(
            presentation.FontService, pool, presentation.GlyphRenderer, componentManager));
        Register<AbilityScoreColumnHeader>((font, elements, glyph) => new AbilityScoreColumnHeader(font, elements, glyph));
        Register<AbilityScoreModifierRow>((font, elements, glyph) => new AbilityScoreModifierRow(font, elements, glyph));
        Register<SeparatorBar>((font, elements, glyph) => new SeparatorBar(font, elements, glyph));
        Register<TextDivider>((font, elements, glyph) => new TextDivider(font, elements, glyph));
        Register<Tooltip>((font, elements, glyph) => new Tooltip(font, elements, glyph));

        pool.RegisterFactory<CorpseInventoryWindow>(() => new CorpseInventoryWindow(
            presentation.FontService, pool, presentation.GlyphRenderer, componentManager,
            presentation.SpriteSheetService, presentation.SpriteRenderer, itemCatalog, world, contextMenuController, mapViewState));
        Register<EntityIconElement>((font, elements, glyph) => new EntityIconElement(
            font, elements, glyph, presentation.SpriteSheetService, presentation.SpriteRenderer,
            componentManager.GetDirectPool<SpriteComponent>(), componentManager.GetDirectPool<GlyphComponent>()));

        Register<InspectionWindow>((font, elements, glyph) => new InspectionWindow(font, elements, glyph, mapViewState));
        Register<ItemDetailsWindow>((font, elements, glyph) => new ItemDetailsWindow(font, elements, glyph, presentation.SpriteSheetService, presentation.SpriteRenderer, actionCatalog));
        Register<ItemIconElement>((font, elements, glyph) => new ItemIconElement(font, elements, glyph, presentation.SpriteSheetService, presentation.SpriteRenderer));
        Register<TargetShapePreviewElement>((font, elements, glyph) => new TargetShapePreviewElement(font, elements, glyph));
        Register<HealthBarElement>((font, elements, glyph) => new HealthBarElement(
            font, elements, glyph,
            componentManager.GetPackedPool<HealthComponent>(),
            componentManager.IsRegistered<StatModifierComponent>() ? componentManager.GetMultiPool<StatModifierComponent>() : null));
    }
}
