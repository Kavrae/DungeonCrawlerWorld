using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;

namespace Presentation.UI.Shops;

/// <summary>
/// Opens a ShopWindow next to the player's own InventoryManagementWindow -- copies
/// SecondaryInventoryWindowController's own toggle/cascade-placement shape (see its own doc
/// comment) rather than being extended by it: a shop never marks LootedComponent (it isn't
/// lootable, it's tradeable) and its own open/close instead drives MapViewState.OpenShopEntityId,
/// the shared flag Phase 4 of the Shops plan reads to switch both grids into their price-showing
/// layout. One target at a time, same toggle-to-close convention as looting.
/// </summary>
public sealed class ShopWindowController(
    ElementPoolService elementPoolService,
    MapViewState mapViewState,
    InventoryFolderController inventoryFolderController,
    ContextMenuController contextMenuController,
    MapWindow mapWindow)
{
    private UiLayerStack _layers = null!;
    private Tooltip _hoverPopup = null!;
    private ShopWindow? _window;
    private int _currentTargetEntityId = -1;

    /// <summary>The currently-open shop window's own target entity id, if any -- lets InventoryFolderController.GetSecondaryTargetEntityId answer "is a secondary window open, and for whom" for the player's own inventory grid's Give/Take menu, the same role SecondaryInventoryWindowController.OpenTargetEntityId already plays for corpses/containers.</summary>
    public int? OpenTargetEntityId => _window is null ? null : _currentTargetEntityId;

    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    /// <summary>Settable late-bound callback for "the player clicked a real single-stack item cell in this shop's own grid" -- see SecondaryInventoryWindowController.OnItemSelected's own doc comment.</summary>
    public Action<int, Guid>? OnItemSelected { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Compare from this shop's own item context menu" -- see SecondaryInventoryWindowController.OnCompareRequested's own doc comment.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    /// <summary>Closes the currently-open shop window, if any -- a no-op otherwise. See SecondaryInventoryWindowController.CloseIfOpen's own doc comment for why ShellBootstrapper needs this (mutual exclusion with a corpse/container window).</summary>
    public void CloseIfOpen() => _window?.Close();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;

        _hoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = PopupChrome.CorpseLootHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);
    }

    /// <summary>
    /// Invoked from a shop's right-click context menu "Shop" option (see MapWindow.OnShopClicked).
    /// Toggles closed if targetEntityId is already the open target; otherwise opens the player's
    /// own inventory window (idempotent) alongside a fresh ShopWindow for targetEntityId --
    /// replacing whichever shop was previously open, if any.
    /// </summary>
    public void OpenShop(int targetEntityId)
    {
        if (_window is not null && _currentTargetEntityId == targetEntityId)
        {
            _window.Close();
            return;
        }

        _window?.Close();

        inventoryFolderController.OpenInventoryWindow();
        if (inventoryFolderController.PlayerInventoryWindow is not { } playerWindow)
        {
            return; // Disabled inventory -- nothing to shop alongside (see InventoryFolderController.IsInventoryDisabled).
        }

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
        window.Configure(targetEntityId, _hoverPopup, (entityId, stackInstanceId) => OnItemSelected?.Invoke(entityId, stackInstanceId), (entityId, stackInstanceId) => OnCompareRequested?.Invoke(entityId, stackInstanceId));
        window.Closed += HandleClosed;
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window);

        _window = window;
        _currentTargetEntityId = targetEntityId;
        mapViewState.OpenShopEntityId = targetEntityId;
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        _hoverPopup.Hide();
        _window = null;
        _currentTargetEntityId = -1;
        mapViewState.OpenShopEntityId = null;
    }
}
