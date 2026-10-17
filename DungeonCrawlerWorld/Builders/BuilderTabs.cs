using Builders.Items;
using Game.Bootstrap;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Workshop;
using UIEngine.Bootstrap;

namespace Builders;

/// <summary>The game's IDE tabs, in strip order, and the element factories their windows need beyond the framework's own.</summary>
public static class BuilderTabs
{
    public static IReadOnlyList<string> Titles { get; } = [ItemsTab.TabTitle];

    /// <summary>Registers the game's element factories with uiEngine's pool and builds every tab over gameSession -- once per IDE run.</summary>
    public static IReadOnlyList<IIdeTab> Create(UiEngineContext uiEngine, GameSession gameSession)
    {
        RegisterElementFactories(uiEngine, gameSession);
        return [new ItemsTab(gameSession.Catalogs.ItemCatalog, gameSession.Catalogs.GameplayTags, gameSession.World.PlayerEntityId)];
    }

    private static void RegisterElementFactories(UiEngineContext uiEngine, GameSession gameSession)
    {
        var elementPool = uiEngine.ElementPoolService;
        var fontService = uiEngine.FontService;
        var labelRenderer = uiEngine.LabelRenderer;
        var catalogs = gameSession.Catalogs;

        elementPool.RegisterFactory(() => new ItemDetailsWindow(fontService, elementPool, labelRenderer, catalogs.ActionCatalog, catalogs.GameplayTags, gameSession.Views.ActionStateView, gameSession.World));
        elementPool.RegisterFactory(() => new ItemIconElement(fontService, elementPool, labelRenderer, uiEngine.SpriteSheetService));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(fontService, elementPool, labelRenderer));
    }
}
