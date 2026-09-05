using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the Inventory button and the InventoryManagementWindow it opens/closes -- the same
/// single-button shape HealthWindowController already established. Folders have been proven out
/// (NotificationCenter's own summary folder) and are no longer needed for a control that only
/// ever opens one thing -- there's nothing to expand into, so a plain Button replaces the Folder
/// this controller used to own jointly with the Ability Score window (see
/// AbilityScoreWindowController, its own sibling now that the two no longer share a Folder).
/// </summary>
public sealed class InventoryWindowController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ItemCatalog itemCatalog,
    MapWindow mapWindow,
    ContextMenuController contextMenuController)
{
    private readonly PackedComponentPool<InventoryDisabledComponent> _disabledPool = componentManager.GetPackedPool<InventoryDisabledComponent>();

    private Button _button = null!;
    private WindowLifecycle<InventoryManagementWindow> _slot = null!;
    private Tooltip _hoverPopup = null!;
    private UiLayerStack _layers = null!;

    /// <summary>The player's own currently-open InventoryManagementWindow, if any -- lets SecondaryInventoryWindowController/ShopWindowController/TradeWindowController/ItemDetailsWindowController/ItemComparisonController/AbilityScoreWindowController position a window relative to it without any of them owning a second instance of their own.</summary>
    public InventoryManagementWindow? PlayerInventoryWindow => _slot.Window;

    /// <summary>
    /// Settable late-bound query for "is a secondary/corpse inventory window currently open, and
    /// for which entity" -- wired by ShellBootstrapper to SecondaryInventoryWindowController.
    /// OpenTargetEntityId once that controller exists (it's built after this one, and itself
    /// depends on this controller, so the two can't reference each other via constructor
    /// injection -- the same settable-delegate shape MapWindow.OnCorpseClicked/OnInspectionOpened
    /// already use to break an identical ordering cycle). Threaded down to the player's own
    /// InventoryManagementWindow via CreateInventoryWindow's Configure call, and from there to
    /// every tab's own InventoryGridContent, whose Give/Take menu calls it fresh on every
    /// right-click rather than caching a stale answer.
    /// </summary>
    public Func<int?>? GetSecondaryTargetEntityId { get; set; }

    /// <summary>Settable late-bound callback for "the player clicked a real single-stack item cell in their own inventory grid" -- wired by ShellBootstrapper to ItemDetailsWindowController.Open once that controller exists (built after this one, and itself depends on this controller's PlayerInventoryWindow accessor, so the two can't reference each other via constructor injection -- same ordering cycle GetSecondaryTargetEntityId already breaks the same way). Threaded down to the player's own InventoryManagementWindow via CreateInventoryWindow's Configure call, and from there to every tab's own InventoryGridContent.</summary>
    public Action<int, Guid>? OnItemSelected { get; set; }

    /// <summary>Settable late-bound callback for "the player chose Compare from an inventory item cell's own context menu" -- wired by ShellBootstrapper to ItemComparisonController.Arm once that controller exists, the same ordering reason OnItemSelected is wired the same way. Threaded the same path.</summary>
    public Action<int, Guid>? OnCompareRequested { get; set; }

    /// <summary>Opens the player's own Inventory window if it isn't already -- idempotent, same as WindowLifecycle.Open itself. Lets a non-button trigger (e.g. clicking a corpse to loot it) reuse this window instead of the button being the only way to open it.</summary>
    public void OpenInventoryWindow() => _slot.Open();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _slot = new WindowLifecycle<InventoryManagementWindow>(CreateInventoryWindow, IsInventoryDisabled, layers, () => { });

        _hoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = InventoryChrome.InventoryHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);

        _button = elementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = InventoryChrome.ButtonPosition, Size = InventoryChrome.ButtonSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Text = new TextOptions { Text = "I" },
            Button = new ButtonOptions { SpriteName = "Inventory" },
        });
        _button.Initialize();
        _button.Clicked += _ => _slot.Toggle();
        layers.Add(UiLayer.DynamicHud, _button);

        // Opening Inventory from this button while another menu window is already open is a
        // normal part of the workflow menu mode exists to support, not something it should block
        // (see UiLayerStack.MarkMenuModeExempt's own doc comment).
        layers.MarkMenuModeExempt(_button);
    }

    /// <summary>Reflects InventoryDisabledComponent on the button itself -- Enabled false both grays the icon (see Button.DrawContent) and excludes it from hit-testing (see Button.IsHitTestable), so a disabled inventory reads as genuinely unclickable rather than clickable-but-silently-refused the way WindowLifecycle's own isDisabled check alone would leave it.</summary>
    public void Update(GameTime gameTime) =>
        _button.Enabled = !IsInventoryDisabled();

    private bool IsInventoryDisabled() => InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId);

    /// <summary>AbilityScoreWindowController computes the identical formula for its own cascading window -- see InventoryChrome.WindowWidthFraction's own doc comment for why the two share the constant rather than each hardcoding their own fraction.</summary>
    private float WindowWidth => mapWindow.CurrentSize.X * InventoryChrome.WindowWidthFraction;

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
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId, _hoverPopup, () => GetSecondaryTargetEntityId?.Invoke(), (entityId, stackInstanceId) => OnItemSelected?.Invoke(entityId, stackInstanceId), (entityId, stackInstanceId) => OnCompareRequested?.Invoke(entityId, stackInstanceId));
        window.Closed += _ => _hoverPopup.Hide(); // Closing the Inventory window mid-hover shouldn't leave the popup stranded.
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        return window;
    }
}
