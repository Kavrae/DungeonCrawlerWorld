using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the player's InventoryManagementWindow: opened and closed by the Inventory menu command (BuiltInMenuCommands), from
/// whichever hotkey slot holds it, and opened by anything else that shows the player's items (looting a corpse).
/// </summary>
public sealed class InventoryWindowController(
    ElementPoolService elementPoolService,
    World world,
    InventoryView inventoryView,
    MapWindow mapWindow,
    ContextMenuController contextMenuController,
    ElementGroup itemWindows)
{
    private WindowLifecycle<InventoryManagementWindow> _windowLifecycle = null!;
    private UiLayerStack _uiLayers = null!;

    /// <summary>The player's own currently-open InventoryManagementWindow, if any -- lets SecondaryInventoryWindowController/ShopWindowController/TradeWindowController/ItemDetailsWindowController/ItemComparisonController/AbilityScoreWindowController position a window relative to it without any of them owning a second instance of their own.</summary>
    public InventoryManagementWindow? PlayerInventoryWindow => _windowLifecycle.Window;

    /// <summary>
    /// Settable late-bound query for "is a secondary/corpse inventory window currently open, and
    /// for which entity" -- wired by ItemWindowCoordinator to SecondaryInventoryWindowController.
    /// OpenTargetEntityId once that controller exists (it's built after this one, and itself
    /// depends on this controller, so the two can't reference each other via constructor
    /// injection -- the same settable-delegate shape MapWindow.OnOpenContainerRequested/OnInspectionOpened
    /// already use to break an identical ordering cycle). Threaded down to the player's own
    /// InventoryManagementWindow via CreateInventoryWindow's Configure call, and from there to
    /// every tab's own InventoryGridContent, whose Give/Take menu calls it fresh on every
    /// right-click rather than caching a stale answer.
    /// </summary>
    public Func<int?>? GetSecondaryTargetEntityId { get; set; }

    /// <summary>Settable late-bound callback for "the player clicked a real single-stack item cell in their own inventory grid" -- wired by ItemWindowCoordinator to ItemDetailsWindowController.Open once that controller exists (built after this one, and itself depends on this controller's PlayerInventoryWindow accessor, so the two can't reference each other via constructor injection -- same ordering cycle GetSecondaryTargetEntityId already breaks the same way). Threaded down to the player's own InventoryManagementWindow via CreateInventoryWindow's Configure call, and from there to every tab's own InventoryGridContent.</summary>
    public Action<int, uint>? OnItemSelected { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Activate (or double-clicked) an inventory item cell" -- wired by ItemWindowCoordinator to arm the item via ActionTargetingController.ArmItemFromStack (which closes this window itself as part of arming -- see its own doc comment), the same ordering reason OnItemSelected is wired the same way. Threaded the same path.</summary>
    public Action<int, uint>? OnActivateRequested { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Activate (or double-clicked) a loot box" -- wired by ItemWindowCoordinator to open every loot box the player holds (see LootboxCommands.OpenAll). Threaded the same path as OnActivateRequested.</summary>
    public Action<int>? OnOpenLootboxesRequested { get; set; }

    /// <summary>Settable late-bound notification fired when the player's Inventory window closes.</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Opens the player's own Inventory window if it isn't already -- idempotent, same as WindowLifecycle.Open itself. Lets something other than the Inventory menu command (clicking a corpse to loot it) open it too.</summary>
    public void OpenInventoryWindow() => _windowLifecycle.Open();

    /// <summary>Opens the Inventory window, or closes it if it is open -- the Inventory menu command.</summary>
    public void ToggleInventoryWindow() => _windowLifecycle.Toggle();

    public void Initialize(UiLayerStack layers)
    {
        _uiLayers = layers;
        _windowLifecycle = new WindowLifecycle<InventoryManagementWindow>(CreateInventoryWindow, IsInventoryDisabled, layers, () => OnClosed?.Invoke(), itemWindows);
    }

    /// <summary>Whether the player's inventory is disabled (InventoryDisabledComponent): its window won't open, and the Inventory menu command greys.</summary>
    public bool IsInventoryDisabled() => inventoryView.IsInventoryDisabled(world.PlayerEntityId);

    private static float WindowWidth => InventoryChrome.WindowWidth;

    private InventoryManagementWindow CreateInventoryWindow()
    {
        var window = elementPoolService.CreateElement<InventoryManagementWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = InventoryChrome.WindowPosition,
                Size = new Vector2(WindowWidth, InventoryChrome.WindowHeight),
                MinimumSize = new Vector2(WindowWidth, InventoryChrome.WindowHeight),
                MaximumSize = mapWindow.CurrentSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Inventory",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(
            world.PlayerEntityId,
            () => GetSecondaryTargetEntityId?.Invoke(),
            (entityId, stackInstanceId) => OnItemSelected?.Invoke(entityId, stackInstanceId),
            (entityId, stackInstanceId) => OnActivateRequested?.Invoke(entityId, stackInstanceId),
            entityId => OnOpenLootboxesRequested?.Invoke(entityId));
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, _uiLayers);
        return window;
    }
}
