using Engine.Tags;
using Microsoft.Xna.Framework;
using UIEngine;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Content;

namespace Presentation.UI.Actions;

/// <summary>The player's Actions window: tabs Menu and Core, a gap, then All and one per action category, each a grid of ActionCells under one shared search and sort row (GridControlToolbar), which keeps its text and focus from tab to tab.</summary>
/// <remarks>
/// It opens on All. The category tabs come from ActionStateView.GetActionCategoryCounts, most populated first, and are
/// rebuilt only when the set of categories changes -- a change that leaves the set alone (an action gained in a category
/// that already has a tab) refreshes only the grids, which watch the player's actions themselves -- so a gained action keeps
/// the tab shown. Close-only, made fresh by ActionsWindowController on each open.
/// </remarks>
public sealed class ActionsWindow(ElementServices services, ActionsWindowServices actionsServices) : Window(services)
{
    /// <summary>All's index: Menu and Core come before it.</summary>
    public const int AllTabIndex = 2;

    private readonly VersionWatcher _actionSetVersionWatcher = new();
    private TabbedContent _tabbedContent = null!;
    private GridControlToolbar _gridToolbar = null!;
    private HashSet<GameplayTag> _currentCategories = [];
    private int _entityId;

    /// <summary>The tabs as last built, in order -- their labels are TabbedContent's.</summary>
    public IReadOnlyList<(string Label, ActionsTab Tab)> Tabs { get; private set; } = [];

    /// <summary>Builds the tabs for entityId's actions. Called after CreateElement and before Initialize.</summary>
    public void Configure(int entityId)
    {
        _entityId = entityId;
        var categoryCounts = actionsServices.ActionStateView.GetActionCategoryCounts(entityId, actionsServices.GameplayTags);
        _currentCategories = ToCategorySet(categoryCounts);
        _gridToolbar = new GridControlToolbar(ElementPoolService, ActionsTabContent.SortOptionLabels, ActionsTabContent.TogglesFor(actionsServices.GridView), ActionsTabContent.SearchGhostText);
        _tabbedContent = new TabbedContent(BuildTabDefinitions(categoryCounts), ElementPoolService, FontService, WindowPalette.PanelBackgroundColor, AllTabIndex, _gridToolbar, GridControl.RowHeight);
        SetContent(_tabbedContent);
        _actionSetVersionWatcher.HasChanged(actionsServices.ActionStateView.GetActionSetVersion(entityId));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!_actionSetVersionWatcher.HasChanged(actionsServices.ActionStateView.GetActionSetVersion(_entityId)))
        {
            return;
        }

        var categoryCounts = actionsServices.ActionStateView.GetActionCategoryCounts(_entityId, actionsServices.GameplayTags);
        var categories = ToCategorySet(categoryCounts);
        if (categories.SetEquals(_currentCategories))
        {
            return;
        }

        _currentCategories = categories;
        _tabbedContent.SetTabs(BuildTabDefinitions(categoryCounts));
    }

    private static HashSet<GameplayTag> ToCategorySet(List<(GameplayTag Tag, int Count)> categoryCounts) => [.. categoryCounts.Select(static entry => entry.Tag)];

    private List<TabbedContent.TabDefinition> BuildTabDefinitions(List<(GameplayTag Tag, int Count)> categoryCounts)
    {
        var tabs = new List<(string Label, ActionsTab Tab)>(categoryCounts.Count + 3)
        {
            ("Menu", ActionsTab.Menu),
            ("Core", ActionsTab.Core),
            ("All", ActionsTab.All),
        };

        foreach (var (category, _) in categoryCounts)
        {
            tabs.Add((actionsServices.GameplayTags.GetPluralDisplayName(category), ActionsTab.OfCategory(category)));
        }

        Tabs = tabs;
        var definitions = new List<TabbedContent.TabDefinition>(tabs.Count);
        for (var index = 0; index < tabs.Count; index++)
        {
            var (label, tab) = tabs[index];
            var grid = new ActionGridContent(actionsServices, ElementPoolService, _entityId, tab);
            definitions.Add(new TabbedContent.TabDefinition(label, new ActionsTabContent(ElementPoolService, grid, _gridToolbar, actionsServices.GridView), StartsGroup: index == AllTabIndex));
        }

        return definitions;
    }
}
