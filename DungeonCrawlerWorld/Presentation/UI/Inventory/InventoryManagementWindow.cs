using Engine.Tags;
using Game.World;
using Microsoft.Xna.Framework;

using Presentation.Input.DragDrop;
using Presentation.UI.Content;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>The player's Inventory window: a TabbedContent with "All" and one tab per item category (Item.* tag) the inventory holds, each an InventoryTabContent, over the Currency row.</summary>
/// <remarks>
/// <para>
/// Every tab shares one GridControlToolbar (count, sort, "Stack Diverged", search) that the TabbedContent holds above the
/// body and never rebuilds, so the sort, the toggle, the search text and the search box's focus carry from tab to tab,
/// and the search applies to whichever tab is shown.
/// </para>
/// <para>
/// The tab list is rebuilt only when the set of categories changes (a category gaining or losing its last stack), not on
/// every inventory version bump: InventoryTagQueries.GetItemCategoryCounts sorts by count, so a quantity change can
/// reorder it, and SetTabs rebuilds every tab. Each tab's grid refreshes its stacks itself. Close-only, made fresh by
/// InventoryWindowController on each open and returned to its pool on close. The Currency row is the footer
/// (SetFooterContent).
/// </para>
/// </remarks>
public sealed class InventoryManagementWindow(
    ElementServices services,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState) : Window(services), IContentDropTarget
{
    private TabbedContent _tabbedContent = null!;
    private GridControlToolbar _gridToolbar = null!;
    private CurrencyRowContent _currencyRowContent = null!;

    private int _entityId;
    private InventoryDropTarget? _dropTarget;
    private Func<int?> _getSecondaryTargetEntityId = static () => null;
    private Action<int, uint> _onItemSelected = static (_, _) => { };
    private Action<int, uint> _onActivateRequested = static (_, _) => { };
    private Action<int> _onOpenLootboxesRequested = static _ => { };
    private readonly VersionWatcher _tagVersionWatcher = new();
    private HashSet<GameplayTag> _currentTags = [];

    /// <summary>Builds this window's content for entityId's inventory. Must be called after CreateElement but before Initialize (see Window.SetContent's own doc comment) -- a fresh TabbedContent per open, since entityId varies across opens of a pooled/reused window instance. getSecondaryTargetEntityId lets each grid's own item context menu (see InventoryGridContent.BuildItemContextMenu) ask "is a secondary/corpse window currently open, and for whom" without this window needing a direct SecondaryInventoryWindowController reference -- see InventoryWindowController.GetSecondaryTargetEntityId, the actual settable source this is expected to be wired to. onItemSelected/onActivateRequested mirror that same settable-delegate shape for selecting the item/closing this window + ActionTargetingController.ArmItemFromStack -- see InventoryWindowController.OnItemSelected/OnActivateRequested. onOpenLootboxesRequested is Activate on a loot box, which opens every loot box the entity holds -- see InventoryWindowController.OnOpenLootboxesRequested.</summary>
    public void Configure(int entityId, Func<int?> getSecondaryTargetEntityId, Action<int, uint> onItemSelected, Action<int, uint> onActivateRequested, Action<int> onOpenLootboxesRequested)
    {
        _entityId = entityId;
        _dropTarget = new InventoryDropTarget(entityId);
        _getSecondaryTargetEntityId = getSecondaryTargetEntityId;
        _onItemSelected = onItemSelected;
        _onActivateRequested = onActivateRequested;
        _onOpenLootboxesRequested = onOpenLootboxesRequested;

        var tagCounts = inventoryServices.InventoryView.GetItemCategoryCounts(entityId, inventoryServices.GameplayTags);
        _currentTags = ToTagSet(tagCounts);
        _gridToolbar = new GridControlToolbar(ElementPoolService, InventoryTabContent.SortOptionLabels, InventoryTabContent.TogglesFor(inventoryServices.GridViews.Inventory), InventoryTabContent.SearchGhostText);
        _tabbedContent = new TabbedContent(BuildTabDefinitions(tagCounts), ElementPoolService, FontService, WindowPalette.PanelBackgroundColor, toolbar: _gridToolbar, toolbarHeight: GridControl.RowHeight);
        _currencyRowContent = new CurrencyRowContent(entityId, inventoryServices, world, contextMenuController, ElementPoolService, _getSecondaryTargetEntityId);
        SetContent(_tabbedContent);
        SetFooterContent(_currencyRowContent, CurrencyRowContent.Height);

        _tagVersionWatcher.HasChanged(CurrentInventoryVersion()); // Primes the baseline so the next Update doesn't immediately rebuild against the list just built above.
    }

    /// <summary>A drop anywhere in this window that its grid and currency row leave unanswered -- its tabs, its border -- lands in this same entity's inventory, item or currency alike.</summary>
    public object? DropTargetFor(object payload, Point position) => _dropTarget;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        _gridToolbar.SetToggleOn(InventoryTabContent.CardViewToggleIndex, ViewSettingInEffect.ShowsCards);

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

    /// <summary>The view this window's grids show and its "Card view" switch sets: the shop's while a shop is open -- so either window's switch changes both sides of a trade -- else the Inventory window's own.</summary>
    private GridViewSetting ViewSettingInEffect => mapViewState.OpenShopEntityId is not null ? inventoryServices.GridViews.Shop : inventoryServices.GridViews.Inventory;

    private InventoryTabContent CreateTabContent(GameplayTag filterTag)
    {
        var gridContent = new InventoryGridContent(world, inventoryServices, ElementPoolService, contextMenuController, _entityId, filterTag, _getSecondaryTargetEntityId, mapViewState, _onItemSelected, _onActivateRequested, _onOpenLootboxesRequested, viewSetting: inventoryServices.GridViews.Inventory);
        return new InventoryTabContent(ElementPoolService, gridContent, _gridToolbar, () => ViewSettingInEffect);
    }
}
