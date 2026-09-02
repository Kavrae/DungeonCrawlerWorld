using Engine.ECS.Components;
using Game.Modules.Death.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;

namespace Presentation.UI.Looting;

/// <summary>
/// Opens a second inventory-grid window next to the player's own InventoryManagementWindow,
/// targeting some other entity -- corpses and containers (treasure chests, lootable while alive
/// or destroyed) today, a shop later reusing this same controller rather than growing its own
/// (see TODO.md's Corpse looting entry, and the InventoryFolderController split entry --
/// deliberately kept separate from InventoryFolderController itself since that class is about the
/// player's own folder/windows and is slated for its own breakup). One target at a time: opening
/// a different target replaces whichever window is currently open; opening the same one again
/// closes it (a toggle, matching this codebase's re-press-to-confirm/cancel convention elsewhere
/// -- e.g. the hotbar). Deliberately owns no knowledge of what's being looted beyond an entity id
/// -- SecondaryInventoryWindow itself owns any target-specific display (name/killer/died-tick).
/// </summary>
public sealed class SecondaryInventoryWindowController(
    ElementPoolService elementPoolService,
    ComponentManager componentManager,
    InventoryFolderController inventoryFolderController,
    ContextMenuController contextMenuController,
    MapWindow mapWindow)
{
    private UiLayerStack _layers = null!;
    private Tooltip _hoverPopup = null!;
    private SecondaryInventoryWindow? _window;
    private int _currentTargetEntityId = -1;

    /// <summary>The currently-open secondary/corpse window's own target entity id, if any -- lets InventoryFolderController's own GetSecondaryTargetEntityId (wired by ShellBootstrapper) answer "is a secondary window open, and for whom" for the player's own inventory grid's Give/Take menu, without that grid needing a direct reference to this controller.</summary>
    public int? OpenTargetEntityId => _window is null ? null : _currentTargetEntityId;

    /// <summary>The currently-open corpse/secondary window's own bounds, if any -- Rectangle.Empty (never contains a click) when nothing is open. Lets ItemDetailsWindowController's own outside-click-close check treat this window as "still inside," the same way it already does for the player's own InventoryManagementWindow.</summary>
    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    /// <summary>Settable late-bound callback for "the player clicked a real single-stack item cell in this corpse/secondary grid" -- see InventoryFolderController.OnItemSelected, wired by ShellBootstrapper to the same ItemDetailsWindowController.Open. Threaded into every corpse window's own Configure call.</summary>
    public Action<int, Guid>? OnItemSelected { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Compare from this corpse/secondary grid's own item context menu" -- see InventoryFolderController.OnCompareRequested, wired by ShellBootstrapper to the same ItemComparisonController.Arm.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    /// <summary>Closes whichever corpse/container window is currently open, if any -- a no-op otherwise. Lets ShellBootstrapper enforce "a corpse/container window and a shop window are never open at once" (both cascade off the same player-inventory-window position, so two open together would overlap) without this controller needing any awareness of ShopWindowController.</summary>
    public void CloseIfOpen() => _window?.Close();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;

        // Created once and shared across every open of a corpse window, the same persistent/
        // toggled-via-IsVisible lifecycle InventoryFolderController's own hover popups use.
        _hoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = PopupChrome.CorpseLootHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);
    }

    /// <summary>
    /// Invoked from a corpse's right-click context menu "Loot" option (see MapWindow.
    /// TryOpenCorpseContextMenuAt). Toggles closed if targetEntityId is already the open target;
    /// otherwise opens the player's own inventory window (idempotent) alongside a fresh
    /// SecondaryInventoryWindow for targetEntityId -- replacing whichever target was previously open,
    /// if any -- and marks it looted.
    /// </summary>
    public void OpenLoot(int targetEntityId)
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
            return; // Disabled inventory -- nothing to loot alongside (see InventoryFolderController.IsInventoryDisabled).
        }

        componentManager.Merge(targetEntityId, new LootedComponent());

        var window = elementPoolService.CreateElement<SecondaryInventoryWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                // Derived from the player window's own live position/size (both user-movable/
                // resizable), not a hardcoded offset, so this stays adjacent even after the
                // player drags their inventory window elsewhere. Singleton window (siblingCount
                // 0), clamped to the map's own screen-bounds proxy so this can't spawn off-screen.
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
        _layers.OpenMenuWindow(window); // A corpse window is Menu Mode, same as the player's own Inventory window.

        _window = window;
        _currentTargetEntityId = targetEntityId;
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        _hoverPopup.Hide(); // Closing mid-hover shouldn't leave the popup stranded -- mirrors InventoryFolderController's own window Closed handlers.
        _window = null;
        _currentTargetEntityId = -1;
    }
}
