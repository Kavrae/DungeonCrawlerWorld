using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.AbilityScores;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Notifications;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the Inventory Folder and the two windows it can open (Inventory, Ability Score) -- the
/// same orchestrating role NotificationCenter plays for its own folder+popups, and the
/// Folder/pooled-window lifecycle is deliberately the same shape: WindowSlot.Open mirrors
/// NotificationCenter.ShowActive, WindowSlot's own close handling mirrors OnActiveNotificationClosed.
///
/// The two tiles and the folder icon are three independent triggers: the Inventory tile toggles
/// only the Inventory window (opens it if closed, closes it if open), the Stats tile toggles
/// only the Ability Score window, and expanding/minimizing the folder itself (its header icon,
/// not either tile -- see Folder's own doc comment) opens/closes both together. This composes
/// cleanly since WindowSlot.Open is idempotent (no-ops if already open) and minimizing only
/// re-fires once both windows are actually closed (see MinimizeFolderIfNothingOpen) -- otherwise
/// closing just one of the two via its own X button would immediately cascade into force-closing
/// the other, which nobody asked for.
/// </summary>
public sealed class InventoryFolderController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ItemCatalog itemCatalog,
    MapWindow mapWindow)
{
    /// <summary>Beneath the Notification folder, with enough clearance that NotificationCenter's own folder never overlaps this one even fully expanded (NotificationCenter.FolderMaximumSize).</summary>
    private static readonly Vector2 FolderGap = new(0, 20);
    private static readonly Vector2 FolderPosition = HudMetrics.Margin + new Vector2(0, NotificationCenter.FolderMaximumSize.Y) + FolderGap;

    private static readonly Vector2 TileSize = new(78, HudMetrics.EntrySize.Y);

    /// <summary>Same reasoning as NotificationCenter.FolderMaximumSize -- a root WrapContent Folder's own MaximumSize is otherwise left at Vector2.Zero. Twice TileSize.Y tall, plus a little breathing room, since the folder now stacks two tiles (Inventory, Stats) rather than one.</summary>
    private static readonly Vector2 FolderMaximumSize = new(200, 180);

    private static readonly Vector2 WindowPosition = new(300, 150);

    /// <summary>Fixed width cap for the Ability Score hover popup; height auto-grows with content -- see HoverPopupWindow.</summary>
    private static readonly Vector2 AbilityScoreHoverPopupMaximumSize = new(220, 10000f);

    /// <summary>Fixed width cap for the Inventory item hover popup; height auto-grows with content -- see HoverPopupWindow.</summary>
    private static readonly Vector2 InventoryHoverPopupMaximumSize = new(220, 10000f);

    /// <summary>Height 30% taller than the original 350 (455) -- more room for the grid now that cells are smaller (see InventoryGridContent.CellSize). Width is no longer fixed -- see WindowWidthFraction.</summary>
    private const float WindowHeight = 455f;

    /// <summary>Both windows take up this fraction of the map window's own width, side by side.</summary>
    private const float WindowWidthFraction = 0.33f;

    /// <summary>Fixed HUD-style gap between the two windows, in the same spirit as GameShellBootstrapper.ActionLockGap -- not tied to map tile size, which changes with zoom.</summary>
    private const float Gap = 12f;

    private readonly DirectComponentPool<InventoryDisabledComponent> _disabledPool = componentManager.GetDirectPool<InventoryDisabledComponent>();

    private Folder _folder = null!;
    private WindowSlot<InventoryManagementWindow> _inventorySlot = null!;
    private WindowSlot<AbilityScoreWindow> _abilityScoreSlot = null!;
    private List<Element> _dynamicHudElements = null!;
    private HoverPopupWindow _abilityScoreHoverPopup = null!;
    private HoverPopupWindow _inventoryHoverPopup = null!;

    public bool IsAnyWindowOpen => _inventorySlot.Window is not null || _abilityScoreSlot.Window is not null;

    public void Initialize(List<Element> dynamicHudElements)
    {
        _dynamicHudElements = dynamicHudElements;
        _inventorySlot = new WindowSlot<InventoryManagementWindow>(CreateInventoryWindow, IsInventoryDisabled, dynamicHudElements, MinimizeFolderIfNothingOpen);
        _abilityScoreSlot = new WindowSlot<AbilityScoreWindow>(CreateAbilityScoreWindow, IsInventoryDisabled, dynamicHudElements, MinimizeFolderIfNothingOpen);

        // Created once and shared across every open/close of the Ability Score window -- same
        // "persistent, toggled via IsVisible" lifecycle as HotbarController's own
        // ArmedHotkeySummaryWindow. Top-level (parent null, see HoverPopupWindow's own doc
        // comment) -- its own ShowNear call re-raises it to the end of dynamicHudElements each
        // time it's shown, so it always draws above AbilityScoreWindow regardless of which was
        // added to this list first.
        _abilityScoreHoverPopup = elementPoolService.CreateElement<HoverPopupWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = AbilityScoreHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _abilityScoreHoverPopup.Initialize();
        dynamicHudElements.Add(_abilityScoreHoverPopup);

        // A separate instance from _abilityScoreHoverPopup -- both windows self-poll the mouse
        // independently every frame, and sharing one popup would let whichever window updates
        // second stomp the other's ShowNear/Hide call when both windows are open side by side.
        _inventoryHoverPopup = elementPoolService.CreateElement<HoverPopupWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = InventoryHoverPopupMaximumSize, DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        _inventoryHoverPopup.Initialize();
        dynamicHudElements.Add(_inventoryHoverPopup);

        _folder = elementPoolService.CreateElement<Folder>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = FolderPosition, MaximumSize = FolderMaximumSize, DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Folder = new FolderOptions { FallbackGlyph = "I", SpriteName = "Inventory" },
        });

        CreateTile("Inventory", _inventorySlot.Toggle);
        CreateTile("Stats", _abilityScoreSlot.Toggle);

        _folder.Initialize();
        dynamicHudElements.Add(_folder);

        _folder.DisplayModeChanged += OnFolderDisplayModeChanged;
    }

    public void Update(GameTime gameTime) =>
        _folder.IsDisabled = IsInventoryDisabled();

    private bool IsInventoryDisabled() => InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId);

    private float WindowWidth => mapWindow.CurrentSize.X * WindowWidthFraction;

    private void CreateTile(string text, Action onClick)
    {
        var tile = elementPoolService.CreateElement<TextWindow>(_folder, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = TileSize, IsTransparent = false },
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
                RelativePosition = WindowPosition,
                Size = new Vector2(WindowWidth, WindowHeight),
                MinimumSize = new Vector2(WindowWidth, WindowHeight),
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
            Content = new ElementContentOptions { ContentColor = InventoryManagementWindow.BackgroundColor },
        });
        window.Configure(world.PlayerEntityId, _inventoryHoverPopup);
        window.Closed += _ => _inventoryHoverPopup.Hide(); // Closing the Inventory window mid-hover shouldn't leave the popup stranded.
        return window;
    }

    private AbilityScoreWindow CreateAbilityScoreWindow()
    {
        var windowWidth = WindowWidth;
        var window = elementPoolService.CreateElement<AbilityScoreWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(WindowPosition.X + windowWidth + Gap, WindowPosition.Y),
                Size = new Vector2(windowWidth, WindowHeight),
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
            Content = new ElementContentOptions { ContentColor = AbilityScoreWindow.BackgroundColor },
        });
        window.Configure(world.PlayerEntityId, _abilityScoreHoverPopup);
        window.Closed += _ => _abilityScoreHoverPopup.Hide(); // Closing the Stats window mid-hover shouldn't leave the popup stranded.
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

    /// <summary>
    /// Generic "one pooled window this controller can open/close/toggle" slot -- shared shape
    /// behind InventoryManagementWindow and AbilityScoreWindow, which otherwise differ only in
    /// their own ElementOptions (createAndConfigure) and disabled predicate. Pooled and reused
    /// for a future open (see ElementPoolService) -- HandleClosed must detach itself, or it stays
    /// subscribed and keeps firing (against a stale Window reference) every time the same
    /// recycled instance is closed again for a later open. Same reasoning as
    /// NotificationCenter.OnActiveNotificationClosed.
    /// </summary>
    private sealed class WindowSlot<TWindow>(Func<TWindow> createAndConfigure, Func<bool> isDisabled, List<Element> dynamicHudElements, Action onClosed)
        where TWindow : Element
    {
        public TWindow? Window { get; private set; }

        public void Open()
        {
            if (Window is not null || isDisabled())
            {
                return;
            }

            var window = createAndConfigure();
            window.Closed += HandleClosed;
            window.Initialize();
            dynamicHudElements.Add(window);
            Window = window;
        }

        public void Toggle()
        {
            if (Window is not null)
            {
                Window.Close();
            }
            else
            {
                Open();
            }
        }

        public void CloseIfOpen() => Window?.Close();

        private void HandleClosed(Element closedWindow)
        {
            closedWindow.Closed -= HandleClosed;
            dynamicHudElements.Remove(closedWindow);
            Window = null;
            onClosed();
        }
    }
}
