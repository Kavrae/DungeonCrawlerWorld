using Builders.Actions;
using Builders.Auras;
using Builders.Items;
using Builders.MapAreas;
using Builders.References;
using Game.Bootstrap;
using Game.World;
using Presentation.UI.Content;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using UIEngine.Bootstrap;
using Workshop;

namespace Builders;

/// <summary>The game's IDE tabs, in strip order, and the element factories their windows need beyond the framework's own.</summary>
public static class BuilderTabs
{
    public static IReadOnlyList<string> Titles { get; } = [ItemsTab.TabTitle, ActionsTab.TabTitle, AurasTab.TabTitle, MapTab.TabTitle];

    /// <summary>Registers the game's element factories with uiEngine's pool and builds every tab over gameSession, sharing one reference index -- once per IDE run.</summary>
    /// <param name="buildPreviewSession">Builds a game session over a map with gameSession's content -- what the Map tab builds each area preview with.</param>
    public static IReadOnlyList<IIdeTab> Create(UiEngineContext uiEngine, GameSession gameSession, Func<Map, GameSession> buildPreviewSession)
    {
        RegisterElementFactories(uiEngine, gameSession, new AreaPreviewCache(buildPreviewSession));
        var catalogs = gameSession.Catalogs;
        var references = new ContentReferenceIndex(catalogs);
        return
        [
            new ItemsTab(catalogs.ItemCatalog, references, catalogs.GameplayTags, gameSession.World.PlayerEntityId),
            new ActionsTab(catalogs.ActionCatalog, references, catalogs.GameplayTags),
            new AurasTab(catalogs.Auras, references, catalogs.GameplayTags),
            new MapTab(catalogs.Areas, references, catalogs.GameplayTags),
        ];
    }

    private static void RegisterElementFactories(UiEngineContext uiEngine, GameSession gameSession, AreaPreviewCache areaPreviews)
    {
        var elementPool = uiEngine.ElementPoolService;
        var fontService = uiEngine.FontService;
        var labelRenderer = uiEngine.LabelRenderer;
        var catalogs = gameSession.Catalogs;

        elementPool.RegisterFactory(() => new ItemDetailsWindow(fontService, elementPool, labelRenderer, catalogs.ActionCatalog, catalogs.GameplayTags, gameSession.Views.ActionStateView, gameSession.World));
        elementPool.RegisterFactory(() => new ActionDetailsWindow(fontService, elementPool, labelRenderer, catalogs.ActionCatalog, catalogs.GameplayTags));
        elementPool.RegisterFactory(() => new AuraDetailsWindow(fontService, elementPool, labelRenderer, catalogs.ActionCatalog, catalogs.GameplayTags));
        elementPool.RegisterFactory(() => new ItemIconElement(fontService, elementPool, labelRenderer, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(fontService, elementPool, labelRenderer));
        elementPool.RegisterFactory(() => new AreaMapView(fontService, elementPool, labelRenderer, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new MapPreviewWindow(fontService, elementPool, labelRenderer, areaPreviews, catalogs));
        elementPool.RegisterFactory(() => new MapDetailsWindow(fontService, elementPool, labelRenderer, catalogs));
        elementPool.RegisterFactory(() => new CellDetailsWindow(fontService, elementPool, labelRenderer));
    }
}
