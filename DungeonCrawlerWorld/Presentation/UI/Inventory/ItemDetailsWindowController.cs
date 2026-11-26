using Game.Modules.Inventory;
using Game.Views;
using Presentation.UI.Content;
using Presentation.UI.Details;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.UI.Inventory;

/// <summary>Owns one Item Details popup, on the details popups' shared lifecycle (DetailsPopupController): one item at a time, at the cursor, closed when the pointer has left both the item and the popup for a moment -- unless pinned.</summary>
/// <remarks>
/// The game has two: Item Details itself and, while comparing, the hovered item's comparison (ItemComparisonController
/// routes hovers between them). It stays open while the pointer is over the item's own cell. With closesOnPressOutside,
/// a press outside every item window closes it too (light dismiss). Without closesWhenPointerLeaves (the comparison
/// popup, kept below the pinned Item Details, out of the pointer's way) it stays open wherever the pointer goes, until it
/// shows another item or is closed. Its width is the player's Inventory window's, or the loot box results window's for a
/// reward.
/// </remarks>
public sealed class ItemDetailsWindowController(
    ElementPoolService elementPoolService,
    InventoryView inventoryView,
    ItemCatalog itemCatalog,
    InventoryWindowController inventoryWindowController,
    MapWindow mapWindow,
    PointerState pointerState,
    ElementGroup itemWindows,
    ItemDetailsModeSetting modeSetting,
    bool closesOnPressOutside = true,
    bool closesWhenPointerLeaves = true,
    bool isComparisonPane = false)
    : DetailsPopupController<ItemDetailsWindow>(elementPoolService, pointerState, itemWindows, () => mapWindow.CurrentSize, closesOnPressOutside, closesWhenPointerLeaves)
{
    /// <summary>How long the pointer may be away from both the item and the popup before it closes (DetailsPopupTiming).</summary>
    internal static int PointerAwayGraceFrames => DetailsPopupTiming.PointerAwayGraceFrames;

    /// <summary>The window the open Item Details takes its width from: the player's Inventory window, or the loot box results window for a reward clicked there.</summary>
    private Window? _widthSourceWindow;

    /// <summary>What the open window shows -- its stack is null for a merged cell's or a used-up reward's definition.</summary>
    private ItemHoverTarget? _shownTarget;

    /// <summary>The item currently shown, if any -- the comparison's anchor identity while pinned.</summary>
    public ItemDefinition? CurrentDefinition => _shownTarget?.Definition;

    public int? CurrentEntityId => _shownTarget?.EntityId;

    public uint? CurrentStackInstanceId => _shownTarget?.StackInstanceId;

    /// <summary>Settable late-bound callback for the window's own "Compare" button -- none on a popup that leaves it unset.</summary>
    public Action<int, uint>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound callback for the window's "Stop Comparison" button, shown while pinned.</summary>
    public Action? OnStopComparisonRequested { get; set; }

    public bool IsShowing(in ItemHoverTarget target) => Window is not null && _shownTarget is { } shown && shown.IsSameCellAs(target);

    /// <summary>Shows target at the cursor, priced by shopPricing while a shop is open, its lines coloured against comparedAgainst.</summary>
    public void OpenAtCursor(in ItemHoverTarget target, IItemShopPricingSource shopPricing, IReadOnlyList<ItemDefinition> comparedAgainst) =>
        Show(target, inventoryWindowController.PlayerInventoryWindow, shopPricing, comparedAgainst);

    /// <summary>Shows stackInstanceId's live stack and effective item, priced by shopPricing while a shop is open -- a no-op if the stack is gone or there's no window to take the width from.</summary>
    /// <param name="widthSourceWindow">The window whose width to take; null for the player's own Inventory window.</param>
    /// <param name="shopPricing">The grid the item was chosen in, or null; an item already shown keeps its own pricing when this is null.</param>
    public void Open(int entityId, uint stackInstanceId, Window? widthSourceWindow = null, IItemShopPricingSource? shopPricing = null)
    {
        if (!inventoryView.TryGetStack(entityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
        {
            return;
        }

        var target = new ItemHoverTarget(entityId, stackInstanceId, definition);
        if (IsShowing(target) && (shopPricing is null || ReferenceEquals(shopPricing, Window!.ShopPricing)))
        {
            return;
        }

        Show(target, widthSourceWindow ?? inventoryWindowController.PlayerInventoryWindow, shopPricing, comparedAgainst: []);
    }

    /// <summary>Shows definition with no stack behind it -- a loot box reward whose stack has since been used up. Read-only: there's no stack to compare.</summary>
    public void OpenDefinition(int entityId, ItemDefinition definition, Window widthSourceWindow) =>
        Show(new ItemHoverTarget(entityId, null, definition), widthSourceWindow, shopPricing: null, comparedAgainst: []);

    /// <summary>Re-Configures the open window with the item it already shows, its lines coloured against comparedAgainst instead -- a no-op if nothing is open.</summary>
    public void UpdateComparedAgainst(IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (Window is not { } window || _shownTarget is not { } shown || _widthSourceWindow is not { } widthSourceWindow)
        {
            return;
        }

        window.Configure(shown.EntityId, shown.StackInstanceId ?? ItemDetailsWindow.NoStackInstanceId, shown.Definition, widthSourceWindow.ContentSize.X, comparedAgainst, window.ShopPricing);
    }

    protected override bool IsPointerOverShown(Element element) =>
        element is InventoryItemStackCell cell && _shownTarget is { } shown && shown.EntityId == cell.EntityId &&
        shown.StackInstanceId == cell.StackInstanceId && (cell.StackInstanceId is not null || shown.Definition.Id == cell.ItemDefinitionId);

    protected override void OnWindowClosed()
    {
        _widthSourceWindow = null;
        _shownTarget = null;
    }

    private void Show(ItemHoverTarget target, Window? widthSourceWindow, IItemShopPricingSource? shopPricing, IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (widthSourceWindow is null)
        {
            return;
        }

        _widthSourceWindow = widthSourceWindow;
        _shownTarget = target;
        var stackInstanceId = target.StackInstanceId ?? ItemDetailsWindow.NoStackInstanceId;
        Show(
            widthSourceWindow,
            window => window.Configure(target.EntityId, stackInstanceId, target.Definition, widthSourceWindow.ContentSize.X, comparedAgainst, shopPricing),
            Wire);
    }

    private void Wire(ItemDetailsWindow window)
    {
        window.OnCompareRequested = OnCompareRequested is null || isComparisonPane
            ? null
            : (compareEntityId, compareStackInstanceId) => OnCompareRequested?.Invoke(compareEntityId, compareStackInstanceId);
        window.IsComparing = () => IsPinned;
        window.OnStopComparisonRequested = () => OnStopComparisonRequested?.Invoke();
        window.ChosenMode = () => modeSetting.Chosen;
        window.OnModeToggleRequested = modeSetting.Toggle;
        window.AlwaysSimple = isComparisonPane;
    }
}
