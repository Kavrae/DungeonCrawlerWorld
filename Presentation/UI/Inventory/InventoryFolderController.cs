using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.AbilityScores;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the Inventory Folder and the two windows it can open (Inventory, Ability Score) -- the
/// same orchestrating role NotificationCenter plays for its own folder+popups, and the
/// Folder/pooled-window lifecycle is deliberately the same shape: WindowLifecycle.Open mirrors
/// NotificationCenter.ShowActive, WindowLifecycle's own close handling mirrors OnActiveNotificationClosed.
///
/// The two tiles and the folder icon are three independent triggers: the Inventory tile toggles
/// only the Inventory window (opens it if closed, closes it if open), the Stats tile toggles
/// only the Ability Score window, and expanding/minimizing the folder itself (its header icon,
/// not either tile -- see Folder's own doc comment) opens/closes both together. This composes
/// cleanly since WindowLifecycle.Open is idempotent (no-ops if already open) and minimizing only
/// re-fires once both windows are actually closed (see MinimizeFolderIfNothingOpen) -- otherwise
/// closing just one of the two via its own X button would immediately cascade into force-closing
/// the other, which nobody asked for.
/// </summary>
public sealed class InventoryFolderController(
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

    private Folder _folder = null!;
    private WindowLifecycle<InventoryManagementWindow> _inventorySlot = null!;
    private WindowLifecycle<AbilityScoreWindow> _abilityScoreSlot = null!;
    private Tooltip _abilityScoreHoverPopup = null!;
    private Tooltip _inventoryHoverPopup = null!;
    private UiLayerStack _layers = null!;

    public bool IsAnyWindowOpen => _inventorySlot.Window is not null || _abilityScoreSlot.Window is not null;

    /// <summary>The player's own currently-open InventoryManagementWindow, if any -- lets SecondaryInventoryWindowController position a corpse/container window relative to it without owning a second instance of its own.</summary>
    public InventoryManagementWindow? PlayerInventoryWindow => _inventorySlot.Window;

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

    /// <summary>Opens the player's own Inventory window if it isn't already -- idempotent, same as WindowLifecycle.Open itself. Lets a non-folder trigger (e.g. clicking a corpse to loot it) reuse this window instead of the folder tile being the only way to open it.</summary>
    public void OpenInventoryWindow() => _inventorySlot.Open();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _inventorySlot = new WindowLifecycle<InventoryManagementWindow>(CreateInventoryWindow, IsInventoryDisabled, layers, MinimizeFolderIfNothingOpen);
        _abilityScoreSlot = new WindowLifecycle<AbilityScoreWindow>(CreateAbilityScoreWindow, IsInventoryDisabled, layers, MinimizeFolderIfNothingOpen);

        // Created once and shared across every open/close of the Ability Score window -- same
        // "persistent, toggled via IsVisible" lifecycle as HotbarController's own Armed Hotkey
        // Summary popup. Top-level (parent null, see Tooltip's own doc comment) -- added to
        // UiLayer.Tooltip, which sits structurally above UiLayer.DynamicHud (where AbilityScoreWindow
        // itself lives), so it always draws above it with no re-raising needed.
        _abilityScoreHoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = InventoryChrome.AbilityScoreHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _abilityScoreHoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _abilityScoreHoverPopup);

        // A separate instance from _abilityScoreHoverPopup -- both windows self-poll the mouse
        // independently every frame, and sharing one popup would let whichever window updates
        // second stomp the other's ShowNear/Hide call when both windows are open side by side.
        _inventoryHoverPopup = elementPoolService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = InventoryChrome.InventoryHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _inventoryHoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _inventoryHoverPopup);

        _folder = elementPoolService.CreateElement<Folder>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = InventoryChrome.FolderPosition, MaximumSize = InventoryChrome.FolderMaximumSize, DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Folder = new FolderOptions { FallbackGlyph = "I", SpriteName = "Inventory" },
        });

        // Initialized before either tile is added -- Element.Initialize/Measure/Arrange now
        // tolerate a child being added at any point relative to its parent's own Initialize and
        // DisplayMode (see Element.Measure's and Element.Initialize's own Minimized guards), so
        // there's no ordering constraint here anymore; this order was chosen to match the rest
        // of the codebase's convention of a control's own Initialize running before its children
        // are attached (see Window.OnChildrenInitialized/GridControl/AbilityScoreWindow).
        _folder.Initialize();
        layers.Add(UiLayer.DynamicHud, _folder);

        // Opening the other window (e.g. Stats while Inventory is already open) from the folder
        // is a normal part of the menu-mode workflow (see UiLayerStack.MarkMenuModeExempt's own
        // doc comment) -- not something an already-open Inventory/Ability Score window should
        // itself block.
        layers.MarkMenuModeExempt(_folder);

        using (_folder.BeginLayoutBatch())
        {
            CreateTile("Inventory", _inventorySlot.Toggle);
            CreateTile("Stats", _abilityScoreSlot.Toggle);
        }

        _folder.DisplayModeChanged += OnFolderDisplayModeChanged;
    }

    public void Update(GameTime gameTime) =>
        _folder.IsDisabled = IsInventoryDisabled();

    private bool IsInventoryDisabled() => InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId);

    private float WindowWidth => mapWindow.CurrentSize.X * InventoryChrome.WindowWidthFraction;

    private void CreateTile(string text, Action onClick)
    {
        var tile = elementPoolService.CreateElement<TextWindow>(_folder, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = InventoryChrome.TileSize, IsTransparent = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = text },
        });
        _folder.AddChild(tile);
        tile.Clicked += _ => onClick();
    }

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
        window.Configure(world.PlayerEntityId, _inventoryHoverPopup, () => GetSecondaryTargetEntityId?.Invoke(), (entityId, stackInstanceId) => OnItemSelected?.Invoke(entityId, stackInstanceId), (entityId, stackInstanceId) => OnCompareRequested?.Invoke(entityId, stackInstanceId));
        window.Closed += _ => _inventoryHoverPopup.Hide(); // Closing the Inventory window mid-hover shouldn't leave the popup stranded.
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        return window;
    }

    private AbilityScoreWindow CreateAbilityScoreWindow()
    {
        var windowWidth = WindowWidth;
        var childSize = new Vector2(windowWidth, InventoryChrome.WindowHeight);

        // Anchored to the live Inventory window's own Rectangle when it's open, so this now
        // follows Inventory if it's been dragged (previously this recomputed a parallel position
        // from the same WindowPosition/WindowWidth constants Inventory itself uses, rather than
        // reading Inventory's own live position -- silently wrong the moment Inventory moved).
        // Falls back to today's exact fixed position, unchanged, when Inventory isn't open (no
        // live window to anchor to) -- still clamped to screen either way.
        var relativePosition = PlayerInventoryWindow is { } playerWindow
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
        window.Configure(world.PlayerEntityId, _abilityScoreHoverPopup);
        window.Closed += _ => _abilityScoreHoverPopup.Hide(); // Closing the Stats window mid-hover shouldn't leave the popup stranded.
        window.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), DynamicHudContextMenus.BuildCloseMenu(window, _layers));
        return window;
    }

    /// <summary>Safe unconditionally -- SetDisplayMode no-ops (and doesn't refire DisplayModeChanged) when the folder is already Minimized, e.g. when this was itself triggered by OnFolderDisplayModeChanged's own force-close below rather than a window's own close button.</summary>
    private void MinimizeFolderIfNothingOpen()
    {
        if (!IsAnyWindowOpen)
        {
            _folder.SetDisplayMode(ElementDisplayMode.Minimized);
        }
    }

    /// <summary>Folder collapsing for any reason (a user directly clicking its header included, not just both windows closing) force-closes whichever of the two is still open -- "closing the folder closes its child windows." Expanding it does the opposite: opens both (each call is itself idempotent, so this composes safely with the user then separately clicking either tile).</summary>
    private void OnFolderDisplayModeChanged(Element folder)
    {
        if (_folder.DisplayMode == ElementDisplayMode.Minimized)
        {
            _inventorySlot.CloseIfOpen();
            _abilityScoreSlot.CloseIfOpen();
        }
        else
        {
            _inventorySlot.Open();
            _abilityScoreSlot.Open();
        }
    }
}
