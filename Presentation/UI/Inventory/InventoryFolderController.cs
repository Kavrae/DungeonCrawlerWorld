using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Notifications;

namespace Presentation.UI.Inventory;

/// <summary>
/// Owns the Inventory Folder and the currently-open InventoryManagementWindow (if any) -- the
/// same orchestrating role NotificationCenter plays for its own folder+popups, and the
/// Folder/pooled-window lifecycle is deliberately the same shape: OpenInventoryWindow mirrors
/// NotificationCenter.ShowActive, OnWindowClosed mirrors OnActiveNotificationClosed. One
/// addition beyond that precedent: collapsing the folder directly (not just closing the window)
/// also force-closes the window, since "closing the folder closes its child windows" here --
/// NotificationCenter's own folder has no such requirement for its category tiles.
/// </summary>
public sealed class InventoryFolderController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ItemCatalog itemCatalog)
{
    /// <summary>Beneath the Notification folder, with enough clearance that NotificationCenter's own folder never overlaps this one even fully expanded (NotificationCenter.FolderMaximumSize).</summary>
    private static readonly Vector2 FolderGap = new(0, 20);
    private static readonly Vector2 FolderPosition = HudMetrics.Margin + new Vector2(0, NotificationCenter.FolderMaximumSize.Y) + FolderGap;

    private static readonly Vector2 TileSize = new(78, HudMetrics.EntrySize.Y);

    /// <summary>Same reasoning as NotificationCenter.FolderMaximumSize -- a root WrapContent Folder's own MaximumSize is otherwise left at Vector2.Zero.</summary>
    private static readonly Vector2 FolderMaximumSize = new(200, 100);

    /// <summary>Height 30% taller than the original 350 (455) -- more room for the grid now that cells are smaller (see InventoryGridContent.CellSize).</summary>
    private static readonly Vector2 WindowSize = new(420, 455);

    private readonly DirectComponentPool<InventoryDisabledComponent> _disabledPool = componentManager.GetDirectPool<InventoryDisabledComponent>();

    private Folder _folder = null!;
    private InventoryManagementWindow? _openWindow;
    private List<Element> _alwaysOnTopElements = null!;

    public bool IsAnyWindowOpen => _openWindow is not null;

    public void Initialize(List<Element> alwaysOnTopElements)
    {
        _alwaysOnTopElements = alwaysOnTopElements;

        _folder = elementPoolService.CreateElement<Folder>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = FolderPosition, MaximumSize = FolderMaximumSize, DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Folder = new FolderOptions { FallbackGlyph = "I", SpriteName = "Inventory" },
        });

        var tile = elementPoolService.CreateElement<TextWindow>(_folder, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = TileSize, IsTransparent = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = Color.LightGray },
            Text = new TextOptions { Text = "Inventory" },
        });
        _folder.AddChild(tile);
        tile.Clicked += _ => OpenInventoryWindow();

        _folder.Initialize();
        alwaysOnTopElements.Add(_folder);

        _folder.DisplayModeChanged += OnFolderDisplayModeChanged;
    }

    public void Update(GameTime gameTime) =>
        _folder.IsDisabled = InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId);

    private void OpenInventoryWindow()
    {
        if (_openWindow is not null || InventoryQueries.IsInventoryDisabled(_disabledPool, world.PlayerEntityId))
        {
            return;
        }

        var window = elementPoolService.CreateElement<InventoryManagementWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(300, 150), Size = WindowSize, DisplayMode = ElementDisplayMode.Fixed },
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
        window.Closed += OnWindowClosed;
        window.Initialize();
        _alwaysOnTopElements.Add(window);
        _openWindow = window;
    }

    private void OnWindowClosed(Element closedWindow)
    {
        // Pooled and reused for a future open (see ElementPoolService) -- must detach itself,
        // or it stays subscribed and keeps firing (against a stale _openWindow reference) every
        // time the same recycled instance is closed again for a later open. Same reasoning as
        // NotificationCenter.OnActiveNotificationClosed.
        closedWindow.Closed -= OnWindowClosed;

        _alwaysOnTopElements.Remove(closedWindow);
        _openWindow = null;

        // Safe unconditionally -- SetDisplayMode no-ops (and doesn't refire DisplayModeChanged)
        // when the folder is already Minimized, e.g. when this close was itself triggered by
        // OnFolderDisplayModeChanged below rather than the window's own close button.
        _folder.SetDisplayMode(ElementDisplayMode.Minimized);
    }

    /// <summary>Folder collapsing for any reason (a user directly clicking its header included, not just the window closing) force-closes the still-open window -- "closing the folder closes its child windows."</summary>
    private void OnFolderDisplayModeChanged(Element folder)
    {
        if (_folder.DisplayMode == ElementDisplayMode.Minimized)
        {
            _openWindow?.Close();
        }
    }
}
