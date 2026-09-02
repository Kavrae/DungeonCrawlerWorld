using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the single, persistent Item Details window -- shows whatever item stack was last clicked
/// in either the player's own inventory grid or an open secondary/corpse grid (see
/// InventoryFolderController.OnItemSelected/SecondaryInventoryWindowController.OnItemSelected,
/// both wired here by ShellBootstrapper). Clicking a different item updates this same window in
/// place rather than opening a second one -- mirrors SecondaryInventoryWindowController's own
/// "one target at a time" shape, but for item selection instead of a loot target. Always opens
/// next to the player's own InventoryManagementWindow (InventoryFolderController.PlayerInventoryWindow),
/// even when the click that opened it came from a secondary/corpse grid -- matches the literal
/// "next to the Inventory Menu" spec rather than whichever grid happened to be clicked. Also
/// drives MapViewState.SelectedItemStackInstanceId, which InventoryGridContent and HotbarContent
/// both read to glow the selected stack wherever it's shown.
/// </summary>
public sealed class ItemDetailsWindowController(
    ElementPoolService elementPoolService,
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    InventoryFolderController inventoryFolderController,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    MapWindow mapWindow)
{
    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    private UiLayerStack _layers = null!;
    private ItemDetailsWindow? _window;

    public bool IsOpen => _window is not null;

    /// <summary>The open window's own bounds -- Rectangle.Empty (never contains a click) when nothing is open.</summary>
    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    /// <summary>The item currently shown, if any -- Item Details Comparison's own anchor identity (ItemComparisonController.Arm/AddOrToggle read these to gate eligibility and detect "is this the anchor itself").</summary>
    public ItemDefinition? CurrentDefinition { get; private set; }

    public int? CurrentEntityId { get; private set; }

    public Guid? CurrentStackInstanceId { get; private set; }

    /// <summary>Settable late-bound query for the currently-open secondary/corpse inventory window's own bounds, if any -- wired by ShellBootstrapper to SecondaryInventoryWindowController.Rectangle once that controller exists (built after this one -- same construction-order reason InventoryFolderController.GetSecondaryTargetEntityId is wired the same way). Rectangle.Empty (never "inside"), not null, when nothing is open or this is never wired (e.g. test setups).</summary>
    public Func<Rectangle>? GetSecondaryInventoryWindowRectangle { get; set; }

    /// <summary>Settable late-bound query for every currently-open Item Details Comparison column's own bounds -- without this, a click on a comparison column would look "outside" this window and wrongly close it, since IsOutsideClick has no other way to know those windows exist.</summary>
    public Func<IReadOnlyList<Rectangle>>? GetComparisonColumnRectangles { get; set; }

    /// <summary>Settable late-bound notification fired when this window closes -- wired by ShellBootstrapper to ItemComparisonController.ClearComparison, since a stale comparison against an item that's no longer even shown doesn't make sense.</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Settable late-bound callback for the anchor window's own "Compare" title button -- wired by ShellBootstrapper to ItemComparisonController.Arm once that controller exists (built after this one). Threaded into the window itself (see ItemDetailsWindow.OnCompareRequested) via a wrapper lambda, not the property's own current value captured once, so re-assignment ordering can never matter.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    public void Initialize(UiLayerStack layers) => _layers = layers;

    /// <summary>
    /// True when clickPosition should close this window -- outside both this window's own bounds
    /// and every inventory window it's tied to (the player's own InventoryManagementWindow, and
    /// an open secondary/corpse window, if any) -- see UiInputController.HandleMousePress, which
    /// calls this only while IsOpen, mirroring ContextMenuController's own outside-click check.
    /// </summary>
    public bool IsOutsideClick(Point clickPosition)
    {
        if (Rectangle.Contains(clickPosition))
        {
            return false;
        }

        if (inventoryFolderController.PlayerInventoryWindow is { } playerWindow && playerWindow.Rectangle.Contains(clickPosition))
        {
            return false;
        }

        if (GetSecondaryInventoryWindowRectangle?.Invoke().Contains(clickPosition) == true)
        {
            return false;
        }

        if (GetComparisonColumnRectangles is { } getColumnRectangles)
        {
            foreach (var columnRectangle in getColumnRectangles())
            {
                if (columnRectangle.Contains(clickPosition))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves stackInstanceId's live stack/effective item and shows it -- creates the window
    /// (positioned next to the player's own Inventory window) if nothing is open yet, or just
    /// updates the existing one's content in place otherwise (per spec: clicking another item
    /// changes the same window's item, never opens a second one). A no-op if the stack can no
    /// longer be resolved (e.g. fully consumed between the click and this call) or the player's
    /// own Inventory window isn't open -- every path that can reach this call requires it open
    /// first (both grids only exist inside/alongside it), but this controller has no other way
    /// to anchor a first-time position if that assumption is ever violated.
    /// </summary>
    public void Open(int entityId, Guid stackInstanceId)
    {
        if (!InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition))
        {
            return;
        }

        if (inventoryFolderController.PlayerInventoryWindow is not { } playerWindow)
        {
            return;
        }

        CurrentDefinition = definition;
        CurrentEntityId = entityId;
        CurrentStackInstanceId = stackInstanceId;

        if (_window is { } existing)
        {
            existing.Configure(entityId, stackInstanceId, definition, playerWindow.ContentSize.X);
            mapViewState.SelectedItemStackInstanceId = stackInstanceId;
            return;
        }

        var window = elementPoolService.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            // ChildrenTileMode.Vertical -- every section ItemDetailsWindow.Rebuild adds stacks
            // top to bottom automatically (see InspectionWindow's own host setup), so section
            // content never needs to compute its own Y position.
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                // Derived from the player window's own live position/size, not a hardcoded
                // offset, the same idiom SecondaryInventoryWindowController.OpenLoot already
                // uses for the corpse window -- stays adjacent even after the player drags
                // their inventory window elsewhere. Singleton window (siblingCount 0), clamped
                // to the map's own screen-bounds proxy via WindowCascadePlacement so this can
                // never spawn off-screen if the player's dragged Inventory near an edge. Clamp
                // size uses height 0, not mapWindow.CurrentSize.Y (the WrapContent growth
                // ceiling, ~the full map height) -- feeding that into the clamp collapses
                // `screenSize.Y - size.Y` to ~0, forcing Y to the very top regardless of the
                // player window's own Top (confirmed by reproduction on the comparison-column
                // version of this same call).
                RelativePosition = WindowCascadePlacement.ComputePosition(
                    playerWindow.Rectangle, new Vector2(playerWindow.CurrentSize.X, 0), 0, mapWindow.CurrentSize),
                // WrapContent, not Fixed -- see ItemDetailsWindow's own doc comment for why:
                // a Fixed-mode window whose own height shrinks between rebuilds re-measures its
                // children against its own (small, stale) content size instead of a stable outer
                // budget, silently clamping a later child's height to 0 once an earlier one's
                // real content pushed it far enough down a tall column -- confirmed by
                // reproduction (Tags rendering on top of Description). WrapContent's own Measure
                // path always threads this MaximumSize through to children unchanged, sidestepping
                // that shrink-feedback loop entirely -- the same mechanism Tooltip already relies
                // on for its own auto-height content. MaximumSize.Y still caps growth at the map's
                // visible area (CanUserScrollVertical covers whatever still overflows that).
                MaximumSize = new Vector2(playerWindow.CurrentSize.X, mapWindow.CurrentSize.Y),
                DisplayMode = ElementDisplayMode.WrapContent,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = false, // WrapContent windows can't be manually resized -- see SetSize's own doc comment.
                CanUserScrollVertical = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(entityId, stackInstanceId, definition, playerWindow.ContentSize.X);
        window.Closed += HandleClosed;
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        window.OnCompareRequested = (compareEntityId, compareStackInstanceId) => OnCompareRequested?.Invoke(compareEntityId, compareStackInstanceId);
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window); // Menu Mode, same as the player's own Inventory window and a corpse window.

        _window = window;
        mapViewState.SelectedItemStackInstanceId = stackInstanceId;
    }

    public void Close() => _window?.Close();

    /// <summary>Re-Configures the already-open window with the same item it's already showing, just a new comparedAgainst set -- Item Details Comparison's own "every column, anchor included, re-colors symmetrically whenever the compared set changes" step. A no-op if nothing is open or the player's own Inventory window isn't (mirrors Open's own guard -- ContentSize.X needs a live source).</summary>
    public void UpdateComparedAgainst(IReadOnlyList<ItemDefinition> comparedAgainst)
    {
        if (_window is not { } window || CurrentDefinition is not { } definition || CurrentEntityId is not { } entityId ||
            CurrentStackInstanceId is not { } stackInstanceId || inventoryFolderController.PlayerInventoryWindow is not { } playerWindow)
        {
            return;
        }

        window.Configure(entityId, stackInstanceId, definition, playerWindow.ContentSize.X, comparedAgainst);
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        _window = null;
        mapViewState.SelectedItemStackInstanceId = null;
        CurrentDefinition = null;
        CurrentEntityId = null;
        CurrentStackInstanceId = null;
        OnClosed?.Invoke();
    }
}
