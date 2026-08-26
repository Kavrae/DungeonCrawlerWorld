using Engine.ECS.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Notifications;

namespace Presentation.UI;

/// <summary>
/// Owns the Health button and the HealthWindow it opens/closes -- the single-button counterpart
/// to InventoryFolderController's Folder+two-window shape (see that class's own doc comment for
/// why it's scoped to just Inventory/Ability Score): Health is a sibling trigger, not a third
/// Folder tile, so it gets its own minimal controller instead of growing InventoryFolderController
/// past its own stated scope. A plain Button, not a Folder -- there's only ever one thing to open,
/// so there's nothing to expand into.
/// </summary>
public sealed class HealthWindowController(
    ElementPoolService elementPoolService,
    World world,
    ComponentManager componentManager,
    FontService fontService,
    LabelRenderer labelRenderer)
{
    /// <summary>Beneath the Notification folder, with enough clearance that NotificationCenter's own folder never overlaps this one even fully expanded (NotificationCenter.FolderMaximumSize) -- the same clearance InventoryFolderController.FolderPosition used to keep for itself before this button took its slot.</summary>
    private static readonly Vector2 NotificationClearanceGap = new(0, 20);

    public static readonly Vector2 ButtonPosition = HudMetrics.Margin + new Vector2(0, NotificationCenter.FolderMaximumSize.Y) + NotificationClearanceGap;

    /// <summary>Square, one HudMetrics.EntrySize row tall -- reads as a real icon button (see Button's own single-glyph ink-centered DrawContent) rather than a wide text tile.</summary>
    public static readonly Vector2 ButtonSize = new(HudMetrics.EntrySize.Y, HudMetrics.EntrySize.Y);

    private static readonly Vector2 WindowPosition = new(300, 150);

    private static readonly Vector2 WindowSize = new(260, 360);

    /// <summary>♥ (U+2665, "black heart suit") -- renders via the default DroidSans font with no Symbola-Emoji fallback needed, unlike Burning/Poison/Paralysis's own emoji glyphs (see FontService).</summary>
    private const string HeartGlyph = "♥";

    private Button _button = null!;
    private WindowLifecycle<HealthWindow> _slot = null!;

    public void Initialize(UiLayerStack layers)
    {
        _slot = new WindowLifecycle<HealthWindow>(CreateHealthWindow, () => false, layers, () => { });

        _button = elementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = ButtonPosition, Size = ButtonSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Text = new TextOptions { Text = HeartGlyph, TextColor = Color.Red },
        });
        _button.Initialize();
        _button.Clicked += _ => _slot.Toggle();
        layers.Add(UiLayer.DynamicHud, _button);

        // Same reasoning as InventoryFolderController's own folder tile -- opening the Health
        // window from this button while another menu window is already open is a normal part of
        // the workflow menu mode exists to support, not something it should block (see
        // UiLayerStack.MarkMenuModeExempt's own doc comment).
        layers.MarkMenuModeExempt(_button);
    }

    private HealthWindow CreateHealthWindow()
    {
        var window = elementPoolService.CreateElement<HealthWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = WindowPosition,
                Size = WindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Health",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
                CanUserScrollVertical = true,
            },
            Content = new ElementContentOptions { ContentColor = HealthWindow.BackgroundColor },
        });
        window.Configure(world.PlayerEntityId);
        return window;
    }
}
