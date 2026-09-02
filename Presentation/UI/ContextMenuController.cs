using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;

namespace Presentation.UI;

/// <summary>
/// Opens/closes the single shared ContextMenu popup -- the "shared mechanics" half of this
/// codebase's context-menu split (see ContextMenuOption's own doc comment for the "distributed
/// content" half: what options actually appear is entirely up to whoever detects the right-click,
/// e.g. MapWindow for a corpse's "Loot", TextBox for Cut/Copy/Paste/Select All). Positioned via
/// the same PopupPositioning math Tooltip already uses, anchored SouthEast of a zero-size
/// Rectangle at the cursor -- the menu's top-left corner pins at the cursor, growing down and to
/// the right, the native OS convention for a context menu.
/// </summary>
public sealed class ContextMenuController(ElementPoolService elementPoolService)
{
    private ContextMenu _menu = null!;

    public bool IsOpen => _menu.IsVisible;

    /// <summary>The live popup itself -- UiInputController reads its Rectangle directly to tell an outside click from one that lands on the menu.</summary>
    internal Element Menu => _menu;

    /// <summary>
    /// Test-only override for the screen bounds Open() clamps against -- lets a test drive the
    /// real Open() option-list/click-routing logic without a real GraphicsDevice (unavailable
    /// headlessly). Production never sets this; Open() falls back to the real Viewport when null.
    /// The same seam PlayerHealthBarContent.Update's dual-overload uses for an identical
    /// constraint, adapted to a property since Open() is invoked deep inside MapWindow/TextBox
    /// rather than called by a test directly.
    /// </summary>
    internal Rectangle? ScreenBoundsOverrideForTests { get; set; }

    public void Initialize(UiLayerStack layers)
    {
        _menu = elementPoolService.CreateElement<ContextMenu>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { IsVisible = false, MaximumSize = PopupChrome.ContextMenuMaximumSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        _menu.Initialize();
        layers.Add(UiLayer.ContextMenu, _menu);
    }

    public void Open(Vector2 cursorPosition, IReadOnlyList<ContextMenuOption> options)
    {
        var cursorRectangle = new Rectangle((int)cursorPosition.X, (int)cursorPosition.Y, 0, 0);
        var menuSize = _menu.MeasureSize(options); // The exact size Show below will apply -- computed ahead of it so flip/clamp below sees the real footprint, not Vector2.Zero.
        var screenBounds = ScreenBoundsOverrideForTests ?? elementPoolService.GraphicsDevice.Viewport.Bounds;
        var topLeft = PopupPositioning.GetPositionWithinBounds(cursorRectangle, menuSize, PopupAnchor.SouthEast, Vector2.Zero, screenBounds);
        _menu.Show(topLeft, options);
    }

    public void Close() => _menu.Hide();
}
