using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;

namespace Presentation.UI.Shops;

/// <summary>
/// Opens a ShopWindow next to the player's own InventoryManagementWindow -- shares
/// SecondaryInventoryWindowController's own toggle/cascade-placement shape via TargetedWindowLifecycle
/// (see that class's own doc comment): a shop never marks LootedComponent (it isn't lootable, it's
/// tradeable) and its own open/close instead drives MapViewState.OpenShopEntityId, the shared flag
/// Phase 4 of the Shops plan reads to switch both grids into their price-showing layout. One target
/// at a time, same toggle-to-close convention as looting.
/// </summary>
public sealed class ShopWindowController(
    ElementPoolService elementPoolService,
    MapViewState mapViewState,
    InventoryWindowController inventoryWindowController,
    ContextMenuController contextMenuController,
    MapWindow mapWindow,
    TooltipController tooltipController)
{
    private TargetedWindowLifecycle<ShopWindow> _slot = null!;

    /// <summary>The currently-open shop window's own target entity id, if any -- lets InventoryWindowController.GetSecondaryTargetEntityId answer "is a secondary window open, and for whom" for the player's own inventory grid's Give/Take menu, the same role SecondaryInventoryWindowController.OpenTargetEntityId already plays for corpses/containers.</summary>
    public int? OpenTargetEntityId => _slot.OpenTargetEntityId;

    public Rectangle Rectangle => _slot.Rectangle;

    /// <summary>Settable late-bound callback for "the player clicked a real single-stack item cell in this shop's own grid" -- see SecondaryInventoryWindowController.OnItemSelected's own doc comment.</summary>
    public Action<int, Guid>? OnItemSelected { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Compare from this shop's own item context menu" -- see SecondaryInventoryWindowController.OnCompareRequested's own doc comment.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound callback fired with the target entity id right after a shop genuinely finishes opening a *new* window -- not on the toggle-closed or disabled-inventory early-return paths in OpenShop below. Wired by ShellBootstrapper to TradeWindowController.Open (PLAN-trade-window.md) so the trade window opens exactly when, and only when, a real shop window did.</summary>
    public Action<int>? OnOpened { get; set; }

    /// <summary>Settable late-bound callback fired at the end of TargetedWindowLifecycle's own HandleClosed, after every other cleanup -- wired by ShellBootstrapper to TradeWindowController's own close-and-unwind, so an open trade never survives its shop window closing (X, Escape, or opening a different shop).</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Closes the currently-open shop window, if any -- a no-op otherwise. See SecondaryInventoryWindowController.CloseIfOpen's own doc comment for why ShellBootstrapper needs this (mutual exclusion with a corpse/container window).</summary>
    public void CloseIfOpen() => _slot.CloseIfOpen();

    /// <summary>Repositions the currently-open shop window, if any -- a no-op otherwise. Lets TradeWindowController re-anchor the shop window beside the trade window once the trade window's own final size is known (PLAN-trade-window.md's "Window layout" section) without this controller needing to expose the Window instance itself.</summary>
    public void SetPosition(Vector2 position) => _slot.SetPosition(position);

    public void Initialize(UiLayerStack layers)
    {
        _slot = new TargetedWindowLifecycle<ShopWindow>(inventoryWindowController, layers, contextMenuController, () =>
        {
            mapViewState.OpenShopEntityId = null;
            OnClosed?.Invoke();
        });
    }

    /// <summary>
    /// Invoked from a shop's right-click context menu "Shop" option (see MapWindow.OnShopClicked).
    /// Toggles closed if targetEntityId is already the open target; otherwise opens the player's
    /// own inventory window (idempotent) alongside a fresh ShopWindow for targetEntityId --
    /// replacing whichever shop was previously open, if any.
    /// </summary>
    public void OpenShop(int targetEntityId) => _slot.OpenOrToggle(targetEntityId, playerWindow => CreateShopWindow(targetEntityId, playerWindow), () =>
    {
        mapViewState.OpenShopEntityId = targetEntityId;
        OnOpened?.Invoke(targetEntityId);
    });

    private ShopWindow CreateShopWindow(int targetEntityId, InventoryManagementWindow playerWindow)
    {
        var window = elementPoolService.CreateElement<ShopWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = WindowCascadePlacement.ComputePosition(playerWindow.Rectangle, playerWindow.CurrentSize, 0, mapWindow.CurrentSize),
                Size = playerWindow.CurrentSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowBorder = true,
                CanUserClose = true,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(targetEntityId, tooltipController, (entityId, stackInstanceId) => OnItemSelected?.Invoke(entityId, stackInstanceId), (entityId, stackInstanceId) => OnCompareRequested?.Invoke(entityId, stackInstanceId));
        return window;
    }
}
