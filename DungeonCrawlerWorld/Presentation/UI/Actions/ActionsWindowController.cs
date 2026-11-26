using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.Inventory;
using UIEngine.Geometry;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Actions;

/// <summary>Owns the player's ActionsWindow: opened and closed by the Actions menu command (BuiltInMenuCommands), from whichever hotkey slot holds it.</summary>
/// <remarks>
/// The window is the Inventory window's size and opens where it opens; opened while the Inventory window is open, it
/// opens to the left of it instead, so the two never cover each other. Nothing disables it. It and Action Details are actionWindows: a press outside them closes Action Details. Closing it clears what it had selected.
/// </remarks>
public sealed class ActionsWindowController(
    ElementPoolService elementPoolService,
    World world,
    MapWindow mapWindow,
    InventoryWindowController inventory,
    ContextMenuController contextMenuController,
    ElementGroup actionWindows,
    MapViewState mapViewState)
{
    private WindowLifecycle<ActionsWindow> _windowLifecycle = null!;
    private UiLayerStack _layers = null!;

    /// <summary>The open Actions window, if any.</summary>
    public ActionsWindow? Window => _windowLifecycle.Window;

    /// <summary>Opens the Actions window, or closes it if it is open -- the Actions menu command.</summary>
    public void ToggleActionsWindow() => _windowLifecycle.Toggle();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _windowLifecycle = new WindowLifecycle<ActionsWindow>(CreateActionsWindow, static () => false, layers, ClearSelection, actionWindows);
    }

    /// <summary>The window closed: nothing in it stays selected, as the Inventory window's close clears its selection.</summary>
    private void ClearSelection()
    {
        mapViewState.SelectedActionId = null;
        mapViewState.SelectedMenuCommandId = null;
    }

    private ActionsWindow CreateActionsWindow()
    {
        var size = new Vector2(InventoryChrome.WindowWidth, InventoryChrome.WindowHeight);
        var position = inventory.PlayerInventoryWindow is { } inventoryWindow
            ? RectangleClamp.ClampInside(new Vector2(inventoryWindow.Rectangle.Left - WindowCascadePlacement.Gap - size.X, inventoryWindow.Rectangle.Top), size, mapWindow.CurrentSize)
            : InventoryChrome.WindowPosition;

        var window = elementPoolService.CreateElement<ActionsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = position,
                Size = size,
                MinimumSize = size,
                MaximumSize = mapWindow.CurrentSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Actions",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId);
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, _layers);
        return window;
    }
}
