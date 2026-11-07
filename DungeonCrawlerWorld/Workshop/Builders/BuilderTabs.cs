using Builders.Actions;
using Builders.Browsing;
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
        var origins = new ContentOrigins(gameSession.Content);
        return
        [
            new ItemsTab(catalogs.ItemCatalog, references, catalogs.GameplayTags, origins, gameSession.World.PlayerEntityId),
            new ActionsTab(catalogs.ActionCatalog, references, catalogs.GameplayTags, origins),
            new AurasTab(catalogs.Auras, references, catalogs.GameplayTags, origins),
            new MapTab(catalogs.Areas, references, catalogs.GameplayTags, origins),
        ];
    }

    private static void RegisterElementFactories(UiEngineContext uiEngine, GameSession gameSession, AreaPreviewCache areaPreviews)
    {
        var elementPool = uiEngine.ElementPoolService;
        var services = uiEngine.ElementServices;
        var catalogs = gameSession.Catalogs;

        elementPool.RegisterFactory(() => new ItemDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags, gameSession.Views.ActionStateView, gameSession.World));
        elementPool.RegisterFactory(() => new ActionDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags));
        elementPool.RegisterFactory(() => new AuraDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags));
        elementPool.RegisterFactory(() => new ItemIconElement(services, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(services));
        elementPool.RegisterFactory(() => new AreaMapView(services, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new MapPreviewWindow(services, areaPreviews, catalogs));
        elementPool.RegisterFactory(() => new MapDetailsWindow(services, catalogs));
        elementPool.RegisterFactory(() => new CellDetailsWindow(services));
    }
}
