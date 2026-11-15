using Builders.Actions;
using Builders.Editing;
using Game.Content;
using Game.Modules.Inventory;
using Builders.Auras;
using Builders.Items;
using Builders.MapAreas;
using Game.Bootstrap;
using Game.World;
using Presentation.UI.Content;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using UIEngine.Bootstrap;
using UIEngine.UI;
using Workshop;

namespace Builders;

/// <summary>The game's tabs and the editing side they share (ContentEditing), built together.</summary>
public sealed record BuilderTabSet(IReadOnlyList<IIdeTab> Tabs, ContentEditing Editing);

/// <summary>The game's IDE tabs, in strip order, and the element factories their windows need beyond the framework's own.</summary>
public static class BuilderTabs
{
    public static IReadOnlyList<string> Titles { get; } = [ItemsTab.TabTitle, ActionsTab.TabTitle, AurasTab.TabTitle, MapTab.TabTitle];

    /// <summary>Registers the game's element factories with uiEngine's pool and builds every tab over workspace's session -- once per IDE run; each rebuilt session replaces the factories before any tab reopens on it (ContentEditing.Update), so no window built over an old one is handed out again.</summary>
    /// <param name="buildPreviewSession">Builds a game session over a map with the session's content -- what the Map tab builds each area preview with.</param>
    /// <param name="workspace">The tables the tabs edit, their undo log and the session they read.</param>
    public static BuilderTabSet Create(UiEngineContext uiEngine, Func<Map, GameSession> buildPreviewSession, ContentWorkspace workspace)
    {
        RegisterElementFactories(uiEngine, workspace.Session, new AreaPreviewCache(buildPreviewSession), replaces: false);
        var editing = new ContentEditing(workspace, session => RegisterElementFactories(uiEngine, session, new AreaPreviewCache(buildPreviewSession), replaces: true));

        var items = new ContentEditingTable<ItemDefinition>(
            workspace,
            GameContent.ItemsTable,
            ItemsTab.TabTitle,
            workspace.Session.Catalogs.ItemCatalog.Definitions,
            item => item.Name,
            item => workspace.Origins.ModOf(GameContent.ItemsTable, item.Id),
            item => workspace.Session.ContentChecks.ProblemsOf(item),
            ActivatableAppearance.Of,
            ActivatableAppearance.Fields,
            ItemsTab.PlaceholderJsonOf);
        items.EvaluateAll();
        var inspectorSource = new GameInspectorSource(workspace, uiEngine.ElementPoolService, uiEngine.SpriteSheetService.Sprites);
        return new BuilderTabSet(
        [
            new ItemsTab(items, editing, inspectorSource, uiEngine.ElementPoolService),
            new ActionsTab(workspace),
            new AurasTab(workspace),
            new MapTab(workspace),
        ], editing);
    }

    /// <summary>The game's factories, those whose windows read the session over session; replaces swaps only those, dropping what the old ones made.</summary>
    private static void RegisterElementFactories(UiEngineContext uiEngine, GameSession session, AreaPreviewCache previews, bool replaces)
    {
        var elementPool = uiEngine.ElementPoolService;
        var services = uiEngine.ElementServices;
        var catalogs = session.Catalogs;

        Register(elementPool, replaces, () => new ItemDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags, session.Views.ActionStateView, session.World));
        Register(elementPool, replaces, () => new ActionDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags));
        Register(elementPool, replaces, () => new AuraDetailsWindow(services, catalogs.ActionCatalog, catalogs.GameplayTags));
        Register(elementPool, replaces, () => new MapPreviewWindow(services, previews, catalogs));
        Register(elementPool, replaces, () => new MapDetailsWindow(services, catalogs));
        if (replaces)
        {
            return;
        }

        elementPool.RegisterFactory(() => new ItemIconElement(services, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(services));
        elementPool.RegisterFactory(() => new AreaMapView(services, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new CellDetailsWindow(services));
    }

    private static void Register<TElement>(ElementPoolService elementPool, bool replaces, Func<TElement> factory)
        where TElement : Element
    {
        if (replaces)
        {
            elementPool.ReplaceFactory(factory);
            return;
        }

        elementPool.RegisterFactory(factory);
    }
}
