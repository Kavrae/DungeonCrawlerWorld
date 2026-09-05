using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;

namespace Presentation.UI;

/// <summary>
/// Generic "one target-entity-keyed window a controller can open/toggle/replace, cascaded beside
/// the player's own Inventory window" lifecycle -- shared shape behind ShopWindow
/// (ShopWindowController) and SecondaryInventoryWindow (SecondaryInventoryWindowController),
/// which otherwise differ only in their own ElementOptions/Configure (createAndConfigure) and
/// whatever extra state they key off the open target (mark-looted vs MapViewState.OpenShopEntityId).
/// Mirrors WindowLifecycle's own split: the target-keyed toggle/replace/cascade-position/context-menu
/// wiring lives here once, an onOpened per call and a fixed onClosed (ctor-supplied, same shape as
/// WindowLifecycle's own) cover what's still genuinely per-controller.
/// </summary>
public sealed class TargetedWindowLifecycle<TWindow>(
    InventoryWindowController inventoryWindowController,
    UiLayerStack layers,
    ContextMenuController contextMenuController,
    Action onClosed)
    where TWindow : Window
{
    private TWindow? _window;
    private int _currentTargetEntityId = -1;

    public TWindow? Window => _window;

    /// <summary>The currently-open window's own target entity id, if any -- null once nothing is open.</summary>
    public int? OpenTargetEntityId => _window is null ? null : _currentTargetEntityId;

    /// <summary>The open window's own bounds -- Rectangle.Empty (never contains a click) when nothing is open.</summary>
    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    public void CloseIfOpen() => _window?.Close();

    public void SetPosition(Vector2 position) => _window?.SetRelativePosition(position);

    /// <summary>
    /// Toggles closed if targetEntityId is already the open target; otherwise opens the player's
    /// own inventory window (idempotent) alongside a fresh window for targetEntityId -- replacing
    /// whichever target was previously open, if any. A no-op beyond that replace if the player's
    /// own Inventory window can't open (e.g. a disabled inventory) -- createAndConfigure is never
    /// called in that case, and onOpened never fires.
    /// </summary>
    public void OpenOrToggle(int targetEntityId, Func<InventoryManagementWindow, TWindow> createAndConfigure, Action? onOpened = null)
    {
        if (_window is not null && _currentTargetEntityId == targetEntityId)
        {
            _window.Close();
            return;
        }

        _window?.Close();

        inventoryWindowController.OpenInventoryWindow();
        if (inventoryWindowController.PlayerInventoryWindow is not { } playerWindow)
        {
            return;
        }

        var window = createAndConfigure(playerWindow);
        window.Closed += HandleClosed;
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, layers);
        window.Initialize();
        layers.Add(UiLayer.DynamicHud, window);
        layers.OpenMenuWindow(window);

        _window = window;
        _currentTargetEntityId = targetEntityId;
        onOpened?.Invoke();
    }

    private void HandleClosed(Element closedWindow)
    {
        layers.Remove(UiLayer.DynamicHud, closedWindow);
        layers.CloseMenuWindow(closedWindow);
        _window = null;
        _currentTargetEntityId = -1;
        onClosed();
    }
}
