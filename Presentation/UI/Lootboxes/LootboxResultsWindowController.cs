using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;
using Presentation.UI.Inventory;

namespace Presentation.UI.Lootboxes;

/// <summary>Owns the one loot box results window: what the player's last opening granted.</summary>
/// <remarks>
/// Opening boxes again while it's open replaces what it shows rather than stacking a second window.
/// It opens beside the player's Inventory window when that's open (where Activate came from), sized
/// to its sections up to a share of the map's height, and is a menu window like the Inventory
/// window itself: it holds the game paused while open and closes with Escape.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxResultsWindowController(
    ElementPoolService elementPoolService,
    LootboxCatalog lootboxCatalog,
    ItemCatalog itemCatalog,
    TooltipController tooltipController,
    ContextMenuController contextMenuController,
    InventoryWindowController inventoryWindowController,
    MapWindow mapWindow,
    IPlayerQuery playerQuery)
{
    private UiLayerStack _layers = null!;

    /// <summary>The open window, if any -- what Item Details docks beside when a reward is clicked.</summary>
    public Window? Window { get; private set; }

    /// <summary>The open window's bounds; Rectangle.Empty when nothing is open.</summary>
    public Rectangle Rectangle => Window?.Rectangle ?? Rectangle.Empty;

    /// <summary>What the open window currently shows, if anything.</summary>
    public LootboxResultsContent? Content { get; private set; }

    /// <summary>Settable late-bound callback for "the player clicked a reward" -- wired by ShellBootstrapper to Item Details.</summary>
    public Action<GrantedItem>? OnRewardClicked { get; set; }

    public void Initialize(UiLayerStack layers) => _layers = layers;

    /// <summary>Shows openedGroups, replacing whatever the window showed before; does nothing when nothing was opened.</summary>
    public void Show(IReadOnlyList<OpenedLootboxGroup> openedGroups)
    {
        if (openedGroups.Count == 0)
        {
            return;
        }

        var previousPosition = Window?.RelativePosition;
        Window?.Close();

        var content = new LootboxResultsContent(openedGroups, playerQuery.PlayerEntityId, lootboxCatalog, itemCatalog, tooltipController, reward => OnRewardClicked?.Invoke(reward));
        var window = elementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = previousPosition ?? ComputePosition(),
                Size = ComputeSize(openedGroups),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Loot Boxes",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMove = true,
                CanUserResize = true,
                CanUserScrollVertical = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.SetContent(content);
        window.Closed += HandleClosed;
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, _layers);
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window);

        Window = window;
        Content = content;
    }

    public void Close() => Window?.Close();

    private Vector2 ComputePosition() =>
        inventoryWindowController.PlayerInventoryWindow is { } playerWindow
            ? WindowCascadePlacement.ComputePosition(playerWindow.Rectangle, new Vector2(LootboxChrome.ContentWidthAtOpen, 0), 0, mapWindow.CurrentSize)
            : InventoryChrome.WindowPosition;

    /// <summary>Tall enough for every section at ColumnsAtOpen columns, up to MaximumHeightFraction of the map.</summary>
    private Vector2 ComputeSize(IReadOnlyList<OpenedLootboxGroup> openedGroups)
    {
        var cellStepHeight = InventoryGridContent.CellSize.Y + InventoryGridContent.CellGap;
        var contentHeight = 0f;
        foreach (var group in openedGroups)
        {
            var rows = (group.Items.Count + LootboxChrome.ColumnsAtOpen - 1) / LootboxChrome.ColumnsAtOpen;
            contentHeight += LootboxChrome.HeaderHeight + rows * cellStepHeight + LootboxChrome.SectionGap;
        }

        var height = System.Math.Min(contentHeight + LootboxChrome.WindowChromeAllowance.Y, mapWindow.CurrentSize.Y * LootboxChrome.MaximumHeightFraction);
        return new Vector2(LootboxChrome.ContentWidthAtOpen + LootboxChrome.WindowChromeAllowance.X, height);
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        if (ReferenceEquals(Window, closedWindow))
        {
            Window = null;
            Content = null;
        }
    }
}
