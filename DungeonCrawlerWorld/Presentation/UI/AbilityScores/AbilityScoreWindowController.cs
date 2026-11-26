using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.Inventory;
using UIEngine.Geometry;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// Owns the AbilityScoreWindow: opened and closed by the Ability Scores menu command (BuiltInMenuCommands). Depends on
/// InventoryWindowController to cascade beside a live Inventory window, and shares its "inventory disabled" gate (a
/// character's stats live behind the same lock as their items).
/// </summary>
public sealed class AbilityScoreWindowController(
    ElementPoolService elementPoolService,
    World world,
    InventoryView inventoryView,
    InventoryWindowController inventory,
    MapWindow mapWindow,
    ContextMenuController contextMenuController,
    TooltipController tooltipController)
{
    /// <summary>How much wider than the Inventory window this one opens: a third wider, so each column has room to read a source's name.</summary>
    private const float WidthRelativeToInventoryWindow = 4f / 3f;

    private WindowLifecycle<AbilityScoreWindow> _slot = null!;
    private UiLayerStack _layers = null!;

    /// <summary>Opens the Ability Scores window, or closes it if it is open -- the Ability Scores menu command.</summary>
    public void ToggleAbilityScoreWindow() => _slot.Toggle();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _slot = new WindowLifecycle<AbilityScoreWindow>(CreateAbilityScoreWindow, IsInventoryDisabled, layers, () => { });
    }

    /// <summary>Whether the player's inventory is disabled, which closes off their ability scores too: the window won't open, and the Ability Scores menu command greys.</summary>
    public bool IsInventoryDisabled() => inventoryView.IsInventoryDisabled(world.PlayerEntityId);

    /// <summary>
    /// Anchored to the live Inventory window's own Rectangle when it's open, so this follows
    /// Inventory if it's been dragged. Falls back to a fixed position beside InventoryChrome's
    /// own WindowPosition when Inventory isn't open (no live window to anchor to) -- still
    /// clamped to screen either way.
    /// </summary>
    private AbilityScoreWindow CreateAbilityScoreWindow()
    {
        var inventoryWindowWidth = InventoryChrome.WindowWidth;
        var childSize = new Vector2(inventoryWindowWidth * WidthRelativeToInventoryWindow, InventoryChrome.WindowHeight);

        var relativePosition = inventory.PlayerInventoryWindow is { } playerWindow
            ? WindowCascadePlacement.ComputePosition(playerWindow.Rectangle, childSize, 0, mapWindow.CurrentSize)
            : RectangleClamp.ClampInside(new Vector2(InventoryChrome.WindowPosition.X + inventoryWindowWidth + WindowCascadePlacement.Gap, InventoryChrome.WindowPosition.Y), childSize, mapWindow.CurrentSize);

        var window = elementPoolService.CreateElement<AbilityScoreWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = relativePosition,
                Size = childSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Ability Scores",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId, tooltipController);
        window.Closed += _ => tooltipController.Hide(window); // Closing the Stats window mid-hover shouldn't leave the popup stranded.
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, _layers);
        return window;
    }
}
