using Engine.ECS.Components;
using Game.Modules.Death.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;

namespace Presentation.UI.Looting;

/// <summary>
/// Opens a second inventory-grid window next to the player's own InventoryManagementWindow,
/// targeting some other entity -- corpses today, a chest/shop later reusing this same controller
/// rather than growing its own (see TODO.md's Corpse looting entry, and the InventoryFolderController
/// split entry -- deliberately kept separate from InventoryFolderController itself since that class
/// is about the player's own folder/windows and is slated for its own breakup). One target at a
/// time: opening a different corpse replaces whichever window is currently open; opening the same
/// one again closes it (a toggle, matching this codebase's re-press-to-confirm/cancel convention
/// elsewhere -- e.g. the hotbar). Deliberately owns no knowledge of what's being looted beyond an
/// entity id -- CorpseInventoryWindow itself is what's corpse-specific.
/// </summary>
public sealed class SecondaryInventoryWindowController(
    ElementPoolService elementPoolService,
    ComponentManager componentManager,
    InventoryFolderController inventoryFolderController)
{
    /// <summary>Fixed HUD-style gap to the right of the player's own inventory window -- same spirit as InventoryFolderController.Gap.</summary>
    private const float Gap = 12f;

    /// <summary>Fixed width cap for the corpse grid's own item hover popup; height auto-grows with content -- see Tooltip, mirroring InventoryFolderController's own hover popup sizing.</summary>
    private static readonly Vector2 HoverPopupMaximumSize = new(220, 10000f);

    private UiLayerStack _layers = null!;
    private Tooltip _hoverPopup = null!;
    private CorpseInventoryWindow? _window;
    private int _currentTargetEntityId = -1;

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;

        // Created once and shared across every open of a corpse window, the same persistent/
        // toggled-via-IsVisible lifecycle InventoryFolderController's own hover popups use.
        _hoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = HoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);
    }

    /// <summary>
    /// Click-to-loot's entry point -- temporary, until a context menu's "Loot" replaces it (see
    /// TODO.md's Context menu entry). Toggles closed if targetEntityId is already the open target;
    /// otherwise opens the player's own inventory window (idempotent) alongside a fresh
    /// CorpseInventoryWindow for targetEntityId -- replacing whichever target was previously open,
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

        componentManager.Merge(targetEntityId, new CorpseLootedComponent());

        var window = elementPoolService.CreateElement<CorpseInventoryWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                // Derived from the player window's own live position/size (both user-movable/
                // resizable), not a hardcoded offset, so this stays adjacent even after the
                // player drags their inventory window elsewhere.
                RelativePosition = playerWindow.RelativePosition + new Vector2(playerWindow.CurrentSize.X + Gap, 0),
                Size = playerWindow.CurrentSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Corpse",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = CorpseInventoryWindow.BackgroundColor },
        });
        window.Configure(targetEntityId, _hoverPopup);
        window.Closed += HandleClosed;
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
