using Engine.ECS.Components;
using Engine.Events;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// The player-facing inventory view: a TabbedContent showing InventoryTabContent (GridControl's
/// count/sort/hide-disabled/search row above an InventoryGridContent), one tab per tag currently
/// carried by the entity's inventory (plus a leading "All" tab) -- see
/// InventoryTagQueries.GetTagCounts. Re-derives the tab list whenever the *set* of tags
/// represented changes (a tag gaining or losing its last carrier), not on every inventory version
/// bump -- GetTagCounts sorts by count descending, so a version bump that only changes a stack's
/// Quantity (no tag gained/lost) can still reorder tagCounts, and TabbedContent.SetTabs always
/// rebuilds every tab's InventoryTabContent/InventoryGridContent/GridControl from scratch even
/// when it preserves the active tab's own selection by label -- discarding that tab's sort order/
/// hide-disabled/search state for no reason (confirmed by live testing: dragging a single item
/// in or out reset the active tab's toggles every time). Each tab's own InventoryGridContent
/// already refreshes its displayed stacks independently via its own version watcher regardless of
/// whether SetTabs runs, so skipping it here only skips the tab *list* rebuild, never the grid
/// contents. Close-only (no minimize) -- created fresh by InventoryFolderController each time the
/// Inventory folder is opened and returned to ElementPoolService's pool on close, mirroring
/// NotificationCenter's active-notification-popup lifecycle rather than staying a permanently-
/// existing hidden window. A dedicated Window subclass (rather than a plain Window hosting
/// TabbedContent via SetContent) purely so Configure/Update have somewhere to live -- the
/// codebase's own convention for when a Window subclass is warranted (MapWindow, TextWindow).
///
/// TabbedContent is hosted directly via SetContent on this window itself; the fixed-height
/// Currency row (see CurrencyRowContent) is hosted via SetFooterContent instead of an extra
/// hand-built nested window (see Element.FooterHeight/Window.SetFooterContent).
/// </summary>
public sealed class InventoryManagementWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    EventBus? eventBus = null) : Window(fontService, elementPoolService, labelRenderer)
{
    private TabbedContent _tabbedContent = null!;
    private CurrencyRowContent _currencyRowContent = null!;

    private int _entityId;
    private Tooltip _hoverPopup = null!;
    private Func<int?> _getSecondaryTargetEntityId = static () => null;
    private Action<int, Guid> _onItemSelected = static (_, _) => { };
    private Action<int, Guid> _onCompareRequested = static (_, _) => { };
    private readonly VersionWatcher _tagVersionWatcher = new();
    private HashSet<Tag> _currentTags = [];

    /// <summary>Builds this window's content for entityId's inventory. Must be called after CreateElement but before Initialize (see Window.SetContent's own doc comment) -- a fresh TabbedContent per open, since entityId varies across opens of a pooled/reused window instance. hoverPopup is owned by InventoryFolderController (created once, top-level, shared across opens) rather than a child of this window -- see Tooltip's own doc comment for why a nested child can't work here. getSecondaryTargetEntityId lets each grid's own item context menu (see InventoryGridContent.BuildItemContextMenu) ask "is a secondary/corpse window currently open, and for whom" without this window needing a direct SecondaryInventoryWindowController reference -- see InventoryFolderController.GetSecondaryTargetEntityId, the actual settable source this is expected to be wired to. onItemSelected/onCompareRequested mirror that same settable-delegate shape for ItemDetailsWindowController.Open/ItemComparisonController.Arm -- see InventoryFolderController.OnItemSelected/OnCompareRequested.</summary>
    public void Configure(int entityId, Tooltip hoverPopup, Func<int?> getSecondaryTargetEntityId, Action<int, Guid> onItemSelected, Action<int, Guid> onCompareRequested)
    {
        _entityId = entityId;
        _hoverPopup = hoverPopup;
        _getSecondaryTargetEntityId = getSecondaryTargetEntityId;
        _onItemSelected = onItemSelected;
        _onCompareRequested = onCompareRequested;

        var tagCounts = InventoryTagQueries.GetTagCounts(componentManager, itemCatalog, entityId);
        _currentTags = ToTagSet(tagCounts);
        _tabbedContent = new TabbedContent(BuildTabDefinitions(tagCounts), elementPoolService, fontService, labelRenderer, WindowPalette.PanelBackgroundColor);
        _currencyRowContent = new CurrencyRowContent(entityId, componentManager, world, contextMenuController, elementPoolService, fontService, labelRenderer, spriteSheetService, spriteRenderer, _getSecondaryTargetEntityId, eventBus);
        SetContent(_tabbedContent);
        SetFooterContent(_currencyRowContent, CurrencyRowContent.Height);

        _tagVersionWatcher.HasChanged(CurrentInventoryVersion()); // Primes the baseline so the next Update doesn't immediately rebuild against the list just built above.
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!_tagVersionWatcher.HasChanged(CurrentInventoryVersion()))
        {
            return;
        }

        var tagCounts = InventoryTagQueries.GetTagCounts(componentManager, itemCatalog, _entityId);
        var newTags = ToTagSet(tagCounts);
        if (newTags.SetEquals(_currentTags))
        {
            return;
        }

        _currentTags = newTags;
        _tabbedContent.SetTabs(BuildTabDefinitions(tagCounts));
    }

    private static HashSet<Tag> ToTagSet(List<(Tag Tag, int Count)> tagCounts)
    {
        var tags = new HashSet<Tag>(tagCounts.Count);
        foreach (var (tag, _) in tagCounts)
        {
            tags.Add(tag);
        }

        return tags;
    }

    private uint CurrentInventoryVersion() => componentManager.GetMultiPool<InventoryItemStackComponent>().GetEntityVersion(_entityId);

    private List<TabbedContent.TabDefinition> BuildTabDefinitions(List<(Tag Tag, int Count)> tagCounts)
    {
        var definitions = new List<TabbedContent.TabDefinition>(tagCounts.Count + 1)
        {
            new("All", CreateTabContent(null)),
        };

        foreach (var (tag, _) in tagCounts)
        {
            definitions.Add(new TabbedContent.TabDefinition(tag.ToString(), CreateTabContent(tag)));
        }

        return definitions;
    }

    private InventoryTabContent CreateTabContent(Tag? filterTag)
    {
        var gridContent = new InventoryGridContent(world, componentManager, itemCatalog, elementPoolService, fontService, labelRenderer, spriteSheetService, spriteRenderer, contextMenuController, _entityId, filterTag, _hoverPopup, _getSecondaryTargetEntityId, mapViewState, _onItemSelected, _onCompareRequested);
        return new InventoryTabContent(elementPoolService, fontService, labelRenderer, gridContent);
    }
}
