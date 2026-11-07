using Engine.Utilities;
using Game.Modules.Inventory;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;
using UIEngine.Geometry;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>Owns one Item Details popup: one item at a time, opened just below and right of the cursor, closed when the pointer has left both the item and the popup for a moment -- unless pinned.</summary>
/// <remarks>
/// The game has two: Item Details itself and, while comparing, the hovered item's comparison (ItemComparisonController
/// routes hovers between them). Showing another item while one is open updates it in place and moves it to the cursor.
/// It stays open while the pointer is over the item's own cell, over the popup itself or over a context menu; otherwise
/// it closes once the pointer has been away for PointerAwayGraceFrames. Pinned (Compare), it moves beside the window
/// holding its item and stays wherever the pointer goes. With closesOnPressOutside, a press outside every item window closes it too (light dismiss).
/// Without closesWhenPointerLeaves (the comparison popup, kept below the pinned Item Details, out of the pointer's way)
/// it stays open wherever the pointer goes, until it shows another item or is closed.
/// Its width is the player's Inventory window's, or the loot box results window's for a reward.
/// </remarks>
public sealed class ItemDetailsWindowController(
    ElementPoolService elementPoolService,
    InventoryView inventoryView,
    ItemCatalog itemCatalog,
    InventoryWindowController inventoryWindowController,
    MapWindow mapWindow,
    PointerState pointerState,
    ElementGroup itemWindows,
    bool closesOnPressOutside = true,
    bool closesWhenPointerLeaves = true) : ILightDismissable
{
    /// <summary>How far below and right of the cursor the popup's top-left lands, so a double-click on the item never lands on the popup's edge.</summary>
    private static readonly Vector2 CursorOffset = new(16, 16);

    /// <summary>How long the pointer may be away from both the item and the popup before it closes -- time to cross the gap CursorOffset leaves.</summary>
    private static readonly int PointerAwayGraceFrames = GameTiming.FramesForSeconds(0.3f);

    private UiLayerStack _layers = null!;
    private ItemDetailsWindow? _window;
    private int _pointerAwayFrames;

    /// <summary>The window the open Item Details takes its width from: the player's Inventory window, or the loot box results window for a reward clicked there.</summary>
    private Window? _widthSourceWindow;

    /// <summary>What the open window shows -- its stack is null for a merged cell's or a used-up reward's definition.</summary>
    private ItemHoverTarget? _shownTarget;

    public bool IsOpen => _window is not null;

    /// <summary>True from PinBeside until the window closes: it stays where it was put, wherever the pointer goes.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>The open window's own bounds -- Rectangle.Empty when nothing is open.</summary>
    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    /// <summary>The open window, if any.</summary>
    public ItemDetailsWindow? Window => _window;

    /// <summary>The item currently shown, if any -- the comparison's anchor identity while pinned.</summary>
    public ItemDefinition? CurrentDefinition => _shownTarget?.Definition;

    public int? CurrentEntityId => _shownTarget?.EntityId;

    public uint? CurrentStackInstanceId => _shownTarget?.StackInstanceId;

    /// <summary>Settable late-bound notification fired when this window closes.</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Settable late-bound: where the popup's top-left goes, kept there every update, instead of beside the cursor -- the comparison popup's, below the pinned Item Details.</summary>
    public Func<Vector2>? FixedPosition { get; set; }

    /// <summary>Settable late-bound callback for the window's own "Compare" button -- none on a popup that leaves it unset.</summary>
    public Action<int, uint>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound callback for the window's "Stop Comparison" button, shown while pinned.</summary>
    public Action? OnStopComparisonRequested { get; set; }

    public void Initialize(UiLayerStack layers) => _layers = layers;

    /// <summary>Every open item window (itemWindows: this one, inventory, loot, shop, trade, loot box results) is inside -- a press anywhere else closes it and goes no further.</summary>
    public bool CountsAsInside(Element element) => itemWindows.Contains(element);

    public void Dismiss() => Close();

    public bool IsShowing(in ItemHoverTarget target) => _window is not null && _shownTarget is { } shown && shown.IsSameCellAs(target);

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
        if (IsShowing(target) && (shopPricing is null || ReferenceEquals(shopPricing, _window!.ShopPricing)))
        {
            return;
        }

        Show(target, widthSourceWindow ?? inventoryWindowController.PlayerInventoryWindow, shopPricing, comparedAgainst: []);
    }

    /// <summary>Shows definition with no stack behind it -- a loot box reward whose stack has since been used up. Read-only: there's no stack to compare.</summary>
    public void OpenDefinition(int entityId, ItemDefinition definition, Window widthSourceWindow) =>
        Show(new ItemHoverTarget(entityId, null, definition), widthSourceWindow, shopPricing: null, comparedAgainst: []);

    /// <summary>Pins the open window beside besideWindow -- to its right, else its left, wherever it overlaps no other item window and stays on screen -- so it stays put, whatever the pointer does, until it closes.</summary>
    public void PinBeside(Window besideWindow)
    {
        if (_window is not { } window)
        {
            return;
        }

        IsPinned = true;
        var size = window.CurrentSize;
        var screenSize = mapWindow.CurrentSize;
        var beside = besideWindow.Rectangle;
        Vector2[] candidates =
        [
            new(beside.Right + WindowCascadePlacement.Gap, beside.Top),
            new(beside.Left - WindowCascadePlacement.Gap - size.X, beside.Top),
        ];

        foreach (var candidate in candidates)
        {
            var clamped = new Vector2(candidate.X, RectangleClamp.ClampInside(candidate, size, screenSize).Y);
            if (clamped.X >= 0 && clamped.X + size.X <= screenSize.X && !OverlapsAnotherItemWindow(window, clamped, size))
            {
                window.SetRelativePosition(clamped);
                return;
            }
        }

        PlaceAt(window, candidates[0]);
    }

    private bool OverlapsAnotherItemWindow(Window window, Vector2 position, Vector2 size)
    {
        var bounds = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
        foreach (var member in itemWindows.Members)
        {
            if (!ReferenceEquals(member, window) && member.IsVisible && member.Rectangle.Intersects(bounds))
            {
                return true;
            }
        }

        return false;
    }

    private void Show(in ItemHoverTarget target, Window? widthSourceWindow, IItemShopPricingSource? shopPricing, IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (widthSourceWindow is null)
        {
            return;
        }

        _widthSourceWindow = widthSourceWindow;
        _shownTarget = target;
        var stackInstanceId = target.StackInstanceId ?? ItemDetailsWindow.NoStackInstanceId;
        var openPosition = FixedPosition?.Invoke() ?? new Vector2(pointerState.CursorPosition.X, pointerState.CursorPosition.Y) + CursorOffset;
        _pointerAwayFrames = 0;

        if (_window is { } existing)
        {
            existing.Configure(target.EntityId, stackInstanceId, target.Definition, widthSourceWindow.ContentSize.X, comparedAgainst, shopPricing);
            PlaceAt(existing, openPosition);
            return;
        }

        var window = elementPoolService.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            // Vertical tiling stacks every section ItemDetailsWindow.Rebuild adds, so no section computes its own Y.
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = openPosition,
                // WrapContent, not Fixed -- see ItemDetailsWindow's own doc comment. MaximumSize.Y caps growth at the
                // map's visible area; CanUserScrollVertical scrolls whatever overflows it.
                MaximumSize = new Vector2(widthSourceWindow.CurrentSize.X, mapWindow.CurrentSize.Y),
                DisplayMode = ElementDisplayMode.WrapContent,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = false,
                ShowBorder = true,
                CanUserClose = true, // No title bar, so no Close button: Escape still closes it.
                CanUserMinimize = false,
                CanUserMove = false,
                CanUserResize = false, // WrapContent windows can't be manually resized -- see SetSize's own doc comment.
                CanUserScrollVertical = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(target.EntityId, stackInstanceId, target.Definition, widthSourceWindow.ContentSize.X, comparedAgainst, shopPricing);
        window.Closed += HandleClosed;
        if (OnCompareRequested is not null)
        {
            window.OnCompareRequested = (compareEntityId, compareStackInstanceId) => OnCompareRequested?.Invoke(compareEntityId, compareStackInstanceId);
        }

        window.OnUpdated = HandleWindowUpdated;
        window.IsComparing = () => IsPinned;
        window.OnStopComparisonRequested = () => OnStopComparisonRequested?.Invoke();
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window); // Menu Mode, same as the player's own Inventory window and a corpse window.
        itemWindows.Join(window);
        if (closesOnPressOutside)
        {
            _layers.OpenLightDismiss(this);
        }
        _window = window;
        PlaceAt(window, openPosition);
    }

    /// <summary>Puts window's top-left at position, moved only as far as it takes to keep it on screen.</summary>
    private void PlaceAt(Window window, Vector2 position)
    {
        var placed = RectangleClamp.ClampInside(position, window.CurrentSize, mapWindow.CurrentSize);
        if (placed != window.RelativePosition)
        {
            window.SetRelativePosition(placed);
        }
    }

    /// <summary>Each update of the open window: keep it on screen as its content grows, and -- unless pinned -- close it once the pointer has left both the shown item and the window.</summary>
    private void HandleWindowUpdated()
    {
        if (_window is not { } window)
        {
            return;
        }

        PlaceAt(window, FixedPosition?.Invoke() ?? window.RelativePosition);

        if (IsPinned || !closesWhenPointerLeaves || IsPointerOverShownItem(window))
        {
            _pointerAwayFrames = 0;
            return;
        }

        _pointerAwayFrames++;
        if (_pointerAwayFrames > PointerAwayGraceFrames)
        {
            Close();
        }
    }

    private bool IsPointerOverShownItem(ItemDetailsWindow window)
    {
        for (var element = pointerState.HoveredElement; element is not null; element = element.ParentElement)
        {
            if (ReferenceEquals(element, window) || element is Dropdown)
            {
                return true;
            }

            if (element is InventoryItemStackCell cell && _shownTarget is { } shown && shown.EntityId == cell.EntityId &&
                shown.StackInstanceId == cell.StackInstanceId && (cell.StackInstanceId is not null || shown.Definition.Id == cell.ItemDefinitionId))
            {
                return true;
            }
        }

        return false;
    }

    public void Close() => _window?.Close();

    /// <summary>Re-Configures the open window with the item it already shows, its lines coloured against comparedAgainst instead -- a no-op if nothing is open.</summary>
    public void UpdateComparedAgainst(IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (_window is not { } window || _shownTarget is not { } shown || _widthSourceWindow is not { } widthSourceWindow)
        {
            return;
        }

        window.Configure(shown.EntityId, shown.StackInstanceId ?? ItemDetailsWindow.NoStackInstanceId, shown.Definition, widthSourceWindow.ContentSize.X, comparedAgainst, window.ShopPricing);
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        itemWindows.Leave(closedWindow);
        if (closesOnPressOutside)
        {
            _layers.CloseLightDismiss(this);
        }
        _window = null;
        _widthSourceWindow = null;
        _shownTarget = null;
        IsPinned = false;
        OnClosed?.Invoke();
    }
}
