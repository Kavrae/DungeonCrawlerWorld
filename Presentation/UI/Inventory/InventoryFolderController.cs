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

    public bool IsAnyWindowOpen => _inventorySlot.Window is not null || _abilityScoreSlot.Window is not null;

    public void Initialize(List<Element> dynamicHudElements)
    {
        _dynamicHudElements = dynamicHudElements;
        _inventorySlot = new WindowSlot<InventoryManagementWindow>(CreateInventoryWindow, IsInventoryDisabled, dynamicHudElements, MinimizeFolderIfNothingOpen);
        _abilityScoreSlot = new WindowSlot<AbilityScoreWindow>(CreateAbilityScoreWindow, IsInventoryDisabled, dynamicHudElements, MinimizeFolderIfNothingOpen);

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
            Layout = new ElementLayoutOptions { RelativePosition = WindowPosition, Size = new Vector2(WindowWidth, WindowHeight), DisplayMode = ElementDisplayMode.Fixed },
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
        window.Configure(world.PlayerEntityId);
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
        window.Configure(world.PlayerEntityId);
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
