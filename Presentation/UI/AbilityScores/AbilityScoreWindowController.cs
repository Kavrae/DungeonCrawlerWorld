using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Inventory;

namespace Presentation.UI.AbilityScores;

/// <summary>
/// Owns the Ability Score button and the AbilityScoreWindow it opens/closes -- the sibling
/// InventoryWindowController used to share a Folder with, before Folders were proven out
/// (NotificationCenter's own summary folder) and removed from both (see
/// InventoryWindowController's own doc comment). Depends on InventoryWindowController purely to
/// read its PlayerInventoryWindow accessor -- this window cascades beside a live Inventory window
/// when one's open, same as before the split -- and to keep sharing the same "inventory disabled"
/// gate, which blocks Ability Score access too (a character's stats have always lived behind the
/// same lock as their items).
/// </summary>
public sealed class AbilityScoreWindowController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    InventoryWindowController inventory,
    MapWindow mapWindow,
    ContextMenuController contextMenuController)
{
    private readonly PackedComponentPool<InventoryDisabledComponent> _disabledPool = componentManager.GetPackedPool<InventoryDisabledComponent>();

    private Button _button = null!;
    private WindowLifecycle<AbilityScoreWindow> _slot = null!;
    private Tooltip _hoverPopup = null!;
    private UiLayerStack _layers = null!;

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _slot = new WindowLifecycle<AbilityScoreWindow>(CreateAbilityScoreWindow, IsInventoryDisabled, layers, () => { });

        _hoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = AbilityScoreChrome.HoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);

        _button = elementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = AbilityScoreChrome.ButtonPosition, Size = AbilityScoreChrome.ButtonSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Text = new TextOptions { Text = "S" },
            Button = new ButtonOptions { SpriteName = "AbilityScore" },
        });
        _button.Initialize();
        _button.Clicked += _ => _slot.Toggle();
        layers.Add(UiLayer.DynamicHud, _button);

        // Same reasoning as InventoryWindowController's own button -- opening Ability Score
        // while another menu window is already open is a normal part of the menu-mode workflow,
        // not something it should block (see UiLayerStack.MarkMenuModeExempt's own doc comment).
        layers.MarkMenuModeExempt(_button);
    }

    /// <summary>Mirrors InventoryWindowController.Update's own reasoning -- Enabled false both grays the icon and excludes it from hit-testing.</summary>
    public void Update(GameTime gameTime) =>
        _button.Enabled = !IsInventoryDisabled();

    private bool IsInventoryDisabled() => InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId);

    /// <summary>
    /// Anchored to the live Inventory window's own Rectangle when it's open, so this follows
    /// Inventory if it's been dragged. Falls back to a fixed position beside InventoryChrome's
    /// own WindowPosition when Inventory isn't open (no live window to anchor to) -- still
    /// clamped to screen either way.
    /// </summary>
    private AbilityScoreWindow CreateAbilityScoreWindow()
    {
        var windowWidth = mapWindow.CurrentSize.X * InventoryChrome.WindowWidthFraction;
        var childSize = new Vector2(windowWidth, InventoryChrome.WindowHeight);

        var relativePosition = inventory.PlayerInventoryWindow is { } playerWindow
            ? WindowCascadePlacement.ComputePosition(playerWindow.Rectangle, childSize, 0, mapWindow.CurrentSize)
            : ScreenBoundsClamp.Clamp(new Vector2(InventoryChrome.WindowPosition.X + windowWidth + WindowCascadePlacement.Gap, InventoryChrome.WindowPosition.Y), childSize, mapWindow.CurrentSize);

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
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId, _hoverPopup);
        window.Closed += _ => _hoverPopup.Hide(); // Closing the Stats window mid-hover shouldn't leave the popup stranded.
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        return window;
    }
}
