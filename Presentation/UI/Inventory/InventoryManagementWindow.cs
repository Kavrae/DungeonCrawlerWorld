using Engine.Tags;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// The player-facing inventory view: a TabbedContent showing InventoryTabContent (GridControl's
/// count/sort/hide-disabled/search row above an InventoryGridContent), one tab per item category
/// (Item.* tag) the entity's inventory holds (plus a leading "All" tab) -- see
/// InventoryTagQueries.GetItemCategoryCounts. Re-derives the tab list whenever the *set* of categories
/// represented changes (a category gaining or losing its last stack), not on every inventory version
/// bump -- GetItemCategoryCounts sorts by count descending, so a version bump that only changes a stack's
/// Quantity (no tag gained/lost) can still reorder tagCounts, and TabbedContent.SetTabs always
/// rebuilds every tab's InventoryTabContent/InventoryGridContent/GridControl from scratch even
/// when it preserves the active tab's own selection by label -- discarding that tab's sort order/
/// hide-disabled/search state for no reason (confirmed by live testing: dragging a single item
/// in or out reset the active tab's toggles every time). Each tab's own InventoryGridContent
/// already refreshes its displayed stacks independently via its own version watcher regardless of
/// whether SetTabs runs, so skipping it here only skips the tab *list* rebuild, never the grid
/// contents. Close-only (no minimize) -- created fresh by InventoryWindowController each time the
/// Inventory button is clicked open and returned to ElementPoolService's pool on close, mirroring
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
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    Engine.ECS.Systems.SimulationClock simulationClock) : Window(fontService, elementPoolService, labelRenderer), IWholeWindowDropTarget
{
    private TabbedContent _tabbedContent = null!;
    private CurrencyRowContent _currencyRowContent = null!;

    private int _entityId;
    private TooltipController _tooltipController = null!;
    private Func<int?> _getSecondaryTargetEntityId = static () => null;
    private Action<int, uint> _onItemSelected = static (_, _) => { };
    private Action<int, uint> _onCompareRequested = static (_, _) => { };
    private Action<int, uint> _onActivateRequested = static (_, _) => { };
    private Action<int> _onOpenLootboxesRequested = static _ => { };
    private readonly VersionWatcher _tagVersionWatcher = new();
    private HashSet<GameplayTag> _currentTags = [];

    /// <summary>Builds this window's content for entityId's inventory. Must be called after CreateElement but before Initialize (see Window.SetContent's own doc comment) -- a fresh TabbedContent per open, since entityId varies across opens of a pooled/reused window instance. tooltipController is the one shared instance every hover-popup consumer in the app shows/hides through (see TooltipController's own doc comment) -- not a child of this window, see Tooltip's own doc comment for why a nested child can't work here. getSecondaryTargetEntityId lets each grid's own item context menu (see InventoryGridContent.BuildItemContextMenu) ask "is a secondary/corpse window currently open, and for whom" without this window needing a direct SecondaryInventoryWindowController reference -- see InventoryWindowController.GetSecondaryTargetEntityId, the actual settable source this is expected to be wired to. onItemSelected/onCompareRequested/onActivateRequested mirror that same settable-delegate shape for ItemDetailsWindowController.Open/ItemComparisonController.Arm/closing this window + ActionTargetingController.ArmItemFromStack -- see InventoryWindowController.OnItemSelected/OnCompareRequested/OnActivateRequested. onOpenLootboxesRequested is Activate on a loot box, which opens every loot box the entity holds -- see InventoryWindowController.OnOpenLootboxesRequested.</summary>
    public void Configure(int entityId, TooltipController tooltipController, Func<int?> getSecondaryTargetEntityId, Action<int, uint> onItemSelected, Action<int, uint> onCompareRequested, Action<int, uint> onActivateRequested, Action<int> onOpenLootboxesRequested)
    {
        _entityId = entityId;
        _tooltipController = tooltipController;
        _getSecondaryTargetEntityId = getSecondaryTargetEntityId;
        _onItemSelected = onItemSelected;
        _onCompareRequested = onCompareRequested;
        _onActivateRequested = onActivateRequested;
        _onOpenLootboxesRequested = onOpenLootboxesRequested;

        var tagCounts = inventoryServices.InventoryView.GetItemCategoryCounts(entityId, inventoryServices.GameplayTags);
        _currentTags = ToTagSet(tagCounts);
        _tabbedContent = new TabbedContent(BuildTabDefinitions(tagCounts), ElementPoolService, FontService, WindowPalette.PanelBackgroundColor);
        _currencyRowContent = new CurrencyRowContent(entityId, inventoryServices, world, contextMenuController, ElementPoolService, _getSecondaryTargetEntityId);
        SetContent(_tabbedContent);
        SetFooterContent(_currencyRowContent, CurrencyRowContent.Height);

        _tagVersionWatcher.HasChanged(CurrentInventoryVersion()); // Primes the baseline so the next Update doesn't immediately rebuild against the list just built above.
    }

    /// <summary>IWholeWindowDropTarget -- a whole-window drop always means this same entity, regardless of which of its tabs happens to be active or where exactly within the window the drop landed.</summary>
    public int ResolveItemDropEntityId(Point dropPosition) => _entityId;

    /// <summary>See ResolveItemDropEntityId -- identical, since this entity's own inventory and currency balance are the same one.</summary>
    public int ResolveCurrencyDropEntityId(Point dropPosition) => _entityId;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!_tagVersionWatcher.HasChanged(CurrentInventoryVersion()))
        {
            return;
        }

        var tagCounts = inventoryServices.InventoryView.GetItemCategoryCounts(_entityId, inventoryServices.GameplayTags);
        var newTags = ToTagSet(tagCounts);
        if (newTags.SetEquals(_currentTags))
        {
            return;
        }

        _currentTags = newTags;
        _tabbedContent.SetTabs(BuildTabDefinitions(tagCounts));
    }

    private static HashSet<GameplayTag> ToTagSet(List<(GameplayTag Tag, int Count)> tagCounts)
    {
        var tags = new HashSet<GameplayTag>(tagCounts.Count);
        foreach (var (tag, _) in tagCounts)
        {
            tags.Add(tag);
        }

        return tags;
    }

    private uint CurrentInventoryVersion() => inventoryServices.InventoryView.GetVersion(_entityId);

    private List<TabbedContent.TabDefinition> BuildTabDefinitions(List<(GameplayTag Tag, int Count)> tagCounts)
    {
        var definitions = new List<TabbedContent.TabDefinition>(tagCounts.Count + 1)
        {
            new("All", CreateTabContent(GameplayTag.None)),
        };

        foreach (var (tag, _) in tagCounts)
        {
            definitions.Add(new TabbedContent.TabDefinition(inventoryServices.GameplayTags.GetDisplayName(tag), CreateTabContent(tag)));
        }

        return definitions;
    }

    private InventoryTabContent CreateTabContent(GameplayTag filterTag)
    {
        var gridContent = new InventoryGridContent(world, inventoryServices, ElementPoolService, contextMenuController, _entityId, filterTag, _tooltipController, _getSecondaryTargetEntityId, mapViewState, _onItemSelected, _onCompareRequested, _onActivateRequested, _onOpenLootboxesRequested, simulationClock: simulationClock);
        return new InventoryTabContent(ElementPoolService, gridContent);
    }
}
