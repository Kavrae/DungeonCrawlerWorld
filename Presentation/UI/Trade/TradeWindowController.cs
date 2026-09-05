using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;
using Presentation.UI.Shops;

namespace Presentation.UI.Trade;

/// <summary>
/// Opens the middle Trade Window alongside a shop -- see PLAN-trade-window.md. Wired by
/// ShellBootstrapper to ShopWindowController.OnOpened/OnClosed (not called directly from
/// MapWindow.OnShopClicked) so this only ever opens when a shop genuinely finished opening a new
/// window, and always closes in lockstep with it.
///
/// All three windows of one trade session (player inventory, shop, trade) always close together,
/// full stop -- closing any one of them (its own X/Escape, Cancel, or Complete) closes the other
/// two as well, confirmed live: Cancel and Complete used to leave the shop/inventory windows open
/// (an earlier, narrower design), but a trade session is scoped to exactly one open shop, so there
/// is never a reason to leave either standing once the trade itself is over, whether it ended by
/// cancelling, completing, or either window closing out from under it. This is a real, ordinary
/// CanUserClose menu window (not exempted or hidden from UiLayerStack's menu-window tracking the
/// way an earlier revision of this class tried) -- Escape's own
/// CloseTopmostClosableWindow/CloseAllClosableWindows both give up entirely (do nothing at all)
/// the moment the topmost menu window has CanUserClose false, so an unclosable trade window
/// sitting on top of the shop/inventory would silently swallow Escape rather than falling through
/// to them.
///
/// Three-way close cascade, guarded against re-entrant double-closes (calling Close() a second
/// time on an Element already mid-close corrupts ElementPoolService's pool -- its Closed event
/// fires before the element is actually returned, so a handler that calls Close() again on the
/// same instance re-enters an in-progress close): each of the three entry points below closes
/// only the *other* two, never the one whose own Closed event got it there, and every downstream
/// call (ShopWindowController.CloseIfOpen, this class's own _window/_subscribedInventoryWindow
/// null-checks) reads current state before acting, so a cascade that loops back around (e.g.
/// HandleInventoryWindowClosed's own shopWindowController.CloseIfOpen() call re-entering
/// CloseForShopClosed) always finds its target already gone and no-ops.
/// </summary>
public sealed class TradeWindowController(
    ElementPoolService elementPoolService,
    InventoryWindowController inventoryWindowController,
    ShopWindowController shopWindowController,
    MapWindow mapWindow,
    int tradeOfferPlayerEntityId,
    int tradeOfferShopEntityId)
{
    /// <summary>
    /// Which of the four ways this window can close is currently in flight -- read (and reset back
    /// to Direct) by HandleWindowClosed to decide two independent things: whether it itself still
    /// needs to cascade-close the shop/inventory windows (every reason except Cascaded -- that one
    /// alone means the caller that triggered this close already owns closing the *other* window
    /// itself, see CloseForShopClosed/HandleInventoryWindowClosed), and whether it should run
    /// ReturnEverythingToOwners (every reason except Complete, which already ran its own swap
    /// instead). See this class's own doc comment for why the cascade guards against double-closing.
    /// </summary>
    private enum CloseReason
    {
        /// <summary>The trade window closed on its own (its own X, or Escape).</summary>
        Direct,

        /// <summary>The player clicked Cancel -- see TradeWindow.OnCancelClicked's own doc comment.</summary>
        Cancel,

        /// <summary>CloseForShopClosed or HandleInventoryWindowClosed already triggered this close as part of their own cascade (the shop or inventory window closed first, out from under this one) -- HandleWindowClosed must not also try to close that same origin window again itself.</summary>
        Cascaded,

        /// <summary>The player clicked Complete -- TradeWindow.CompleteTrade already swapped everything before this fires (see TradeWindow.OnCompleteClicked's own doc comment), so HandleWindowClosed must NOT also run ReturnEverythingToOwners here (that would undo the swap it just did).</summary>
        Complete,
    }

    private UiLayerStack _layers = null!;
    private Tooltip _playerColumnHoverPopup = null!;
    private Tooltip _shopColumnHoverPopup = null!;
    private TradeWindow? _window;
    private InventoryManagementWindow? _subscribedInventoryWindow;
    private CloseReason _pendingCloseReason = CloseReason.Direct;

    /// <summary>
    /// Two separate Tooltip instances, not one shared between both columns -- confirmed live the
    /// exact bug InventoryWindowController's/AbilityScoreWindowController's own separate _hoverPopup
    /// fields already exist to avoid ("both windows self-poll the mouse independently every frame, and
    /// sharing one popup would let whichever window updates second stomp the other's ShowNear/Hide
    /// call"): TradeWindow._children updates the shop-side column's InventoryGridContent after the
    /// player-side one every frame, so a shared popup meant the shop-side grid's own Hide() (fired
    /// whenever nothing under it is hovered) permanently overwrote whatever the player-side grid
    /// had just tried to show that same frame -- reproducing as "the tooltip never appears" until
    /// something reordered TradeWindow's own _children (e.g. clicking empty space inside that grid
    /// window directly, raising it past its sibling), at which point the previously-losing column's
    /// own Update call started running last instead, and its own decision finally stuck.
    /// </summary>
    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;

        _playerColumnHoverPopup = CreateHoverPopup();
        layers.Add(UiLayer.Tooltip, _playerColumnHoverPopup);

        _shopColumnHoverPopup = CreateHoverPopup();
        layers.Add(UiLayer.Tooltip, _shopColumnHoverPopup);
    }

    private Tooltip CreateHoverPopup()
    {
        var popup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = PopupChrome.CorpseLootHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        popup.Initialize();
        return popup;
    }

    /// <summary>
    /// Opens the trade window centered on screen, then re-anchors the player's own inventory
    /// window (left) and the just-opened shop window (right) beside it, top edges aligned -- see
    /// PLAN-trade-window.md's "Window layout" section. Subscribes to the inventory window's own
    /// Closed event in addition to relying on ShopWindowController.OnClosed: if the player's
    /// inventory window could close and reopen fresh while a trade with one shop is still
    /// mid-flight, walking up to a second, different shop must never resume or get confused by
    /// stale trade state left over from the first -- closing either half of the pair always fully
    /// closes the trade (and, per this class's own doc comment, the third window too).
    /// </summary>
    public void Open(int shopEntityId)
    {
        if (inventoryWindowController.PlayerInventoryWindow is not { } inventoryWindow)
        {
            return; // Disabled inventory -- ShopWindowController.OpenShop wouldn't have opened a shop window either in this case.
        }

        var window = elementPoolService.CreateElement<TradeWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Trade",
                ShowBorder = true,
                CanUserClose = true, // See this class's own doc comment -- must be true, or Escape gives up entirely rather than falling through to the shop/inventory windows.
                CanUserMove = true,
                CanUserResize = false, // Confirmed -- see PLAN-trade-window.md.
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(tradeOfferPlayerEntityId, tradeOfferShopEntityId, shopEntityId, _playerColumnHoverPopup, _shopColumnHoverPopup);
        window.OnCancelClicked = HandleCancelClicked;
        window.OnCompleteClicked = HandleCompleteClicked;
        window.Closed += HandleWindowClosed;
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);

        var screenSize = mapWindow.CurrentSize;
        var centeredPosition = ScreenBoundsClamp.Clamp((screenSize - window.CurrentSize) / 2f, window.CurrentSize, screenSize);
        window.SetRelativePosition(centeredPosition);

        var leftPosition = new Vector2(centeredPosition.X - WindowCascadePlacement.Gap - inventoryWindow.CurrentSize.X, centeredPosition.Y);
        inventoryWindow.SetRelativePosition(ScreenBoundsClamp.Clamp(leftPosition, inventoryWindow.CurrentSize, screenSize));

        var shopSize = new Vector2(shopWindowController.Rectangle.Width, shopWindowController.Rectangle.Height);
        var rightPosition = new Vector2(centeredPosition.X + window.CurrentSize.X + WindowCascadePlacement.Gap, centeredPosition.Y);
        shopWindowController.SetPosition(ScreenBoundsClamp.Clamp(rightPosition, shopSize, screenSize));

        _window = window;
        _pendingCloseReason = CloseReason.Direct;
        _subscribedInventoryWindow = inventoryWindow;
        inventoryWindow.Closed += HandleInventoryWindowClosed;
    }

    /// <summary>Wired to ShopWindowController.OnClosed -- a no-op if no trade is open (e.g. the inventory-close path below already tore it down this same frame). Closes the trade window, then the inventory window too (the shop already closed itself by the time this runs) -- never re-closes the shop.</summary>
    public void CloseForShopClosed()
    {
        if (_window is null)
        {
            return;
        }

        _pendingCloseReason = CloseReason.Cascaded;
        var inventoryWindow = _subscribedInventoryWindow;
        _window.Close();
        inventoryWindow?.Close();
    }

    /// <summary>The player's own inventory window closed on its own -- close the trade window and the shop window too, never re-close inventory itself.</summary>
    private void HandleInventoryWindowClosed(Element _)
    {
        if (_window is null)
        {
            return;
        }

        _pendingCloseReason = CloseReason.Cascaded;
        _window.Close();
        shopWindowController.CloseIfOpen();
    }

    /// <summary>The player clicked Cancel -- see TradeWindow.OnCancelClicked's own doc comment. Closes all three windows, same as the trade window's own X/Escape -- see this class's own doc comment for why Cancel is no longer a narrower, trade-window-only close.</summary>
    private void HandleCancelClicked()
    {
        _pendingCloseReason = CloseReason.Cancel;
        _window?.Close();
    }

    /// <summary>The player clicked Complete -- TradeWindow.CompleteTrade already ran the swap before this fires (see TradeWindow.OnCompleteClicked's own doc comment). Closes all three windows, same as Cancel -- see this class's own doc comment.</summary>
    private void HandleCompleteClicked()
    {
        _pendingCloseReason = CloseReason.Complete;
        _window?.Close();
    }

    /// <summary>
    /// The single teardown funnel for every way the trade window itself closes (its own X,
    /// Escape, Cancel, Complete, or as a side effect of CloseForShopClosed/HandleInventoryWindowClosed
    /// calling Close() directly) -- always does the trade-window-local cleanup, then cascade-closes
    /// the shop/inventory windows for every reason except Cascaded (the one case where the shop or
    /// inventory window already closed itself first and owns closing the *other* one -- see the
    /// enum's own doc comment; re-closing here would double-close whichever one originated this).
    ///
    /// Runs TradeWindow.ReturnEverythingToOwners for every reason except Complete (which already
    /// ran its own swap instead) -- Cancel, Direct (the window's own X/Escape), and Cascaded
    /// (the shop or inventory window closing out from under it) all mean the trade never finished,
    /// so whatever's still staged in either trade-offer entity must go back to its real owner
    /// before this window is actually returned to the pool. Read from _window (not closedWindow)
    /// while it's still non-null, since that field is about to be cleared below.
    /// </summary>
    private void HandleWindowClosed(Element closedWindow)
    {
        var reason = _pendingCloseReason;
        if (reason != CloseReason.Complete)
        {
            _window?.ReturnEverythingToOwners();
        }

        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        _playerColumnHoverPopup.Hide();
        _shopColumnHoverPopup.Hide();

        var inventoryWindow = _subscribedInventoryWindow;
        if (inventoryWindow is not null)
        {
            inventoryWindow.Closed -= HandleInventoryWindowClosed;
            _subscribedInventoryWindow = null;
        }

        _pendingCloseReason = CloseReason.Direct;
        _window = null;

        if (reason != CloseReason.Cascaded)
        {
            inventoryWindow?.Close();
            shopWindowController.CloseIfOpen();
        }
    }
}
