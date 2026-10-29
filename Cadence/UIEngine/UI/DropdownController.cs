using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>
/// Opens/closes the single shared Dropdown popup -- the shared mechanics; what options appear is up to the caller (a
/// right-click's context menu through ContextMenuController, a DropdownField's choices). One is open at a time: opening
/// another replaces it. UiInputController applies the framework rules while one is open -- Escape and a press outside
/// it close it, and Up/Down/Enter reach it before anything else (see HandleNavigationKey).
/// </summary>
/// <remarks>
/// Positioned with the same PopupPositioning math Tooltip uses: at a point it is anchored SouthEast of a zero-size
/// rectangle there (its top-left pinned at the point, growing down and right, the native OS convention for a context
/// menu); below an element it is anchored to the element's bottom-left, flipping above it when there is no room below.
/// </remarks>
public sealed class DropdownController(ElementPoolService elementPoolService, UiRenderService renderService) : ILightDismissable
{
    private Dropdown _menu = null!;

    private UiLayerStack _layers = null!;

    /// <summary>Where the open dropdown was placed, so a submenu opens the same way.</summary>
    private Rectangle _placementAnchor;

    private bool _placedBelowAnchor;

    private float _minimumWidth;

    private float _maximumHeight = float.MaxValue;

    public bool IsOpen => _menu.IsVisible;

    /// <summary>The live popup itself.</summary>
    internal Dropdown Menu => _menu;

    /// <summary>The element the open dropdown was opened under, or null for one opened at a point. A press on it is not an outside press, so the element can close what it opened rather than reopening it.</summary>
    public Element? Owner { get; private set; }

    /// <summary>
    /// Test-only override for the screen bounds opening clamps against -- lets a test drive the
    /// real option-list/click-routing logic without a real GraphicsDevice (unavailable
    /// headlessly). Production never sets this; opening falls back to the real Viewport when null.
    /// A property rather than a parameter, since opening is invoked deep inside the elements that open menus.
    /// </summary>
    internal Rectangle? ScreenBoundsOverrideForTests { get; set; }

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _menu = elementPoolService.CreateElement<Dropdown>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { IsVisible = false, MaximumSize = PopupChrome.DropdownMaximumSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        _menu.Initialize();
        _menu.OpenSubmenu = options => Place(options, markedIndex: -1);
        layers.Add(UiLayer.ContextMenu, _menu);
    }

    /// <summary>Opens options with the dropdown's top-left at point (flipped and clamped to stay on screen) -- a context menu.</summary>
    public void OpenAt(Vector2 point, IReadOnlyList<DropdownOption> options)
    {
        Owner = null;
        _placementAnchor = new Rectangle((int)point.X, (int)point.Y, 0, 0);
        _placedBelowAnchor = false;
        _minimumWidth = 0f;
        _maximumHeight = float.MaxValue;
        Place(options, markedIndex: -1);
    }

    /// <summary>Opens options under owner, at least as wide as it, scrolling past maximumHeight, with markedIndex's option marked and highlighted (-1 for none).</summary>
    public void OpenBelow(Element owner, IReadOnlyList<DropdownOption> options, int markedIndex = -1, float maximumHeight = float.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Owner = owner;
        _placementAnchor = owner.Rectangle;
        _placedBelowAnchor = true;
        _minimumWidth = owner.Rectangle.Width;
        _maximumHeight = maximumHeight;
        Place(options, markedIndex);
    }

    private void Place(IReadOnlyList<DropdownOption> options, int markedIndex)
    {
        var menuSize = _menu.MeasureSize(options, _minimumWidth, _maximumHeight); // The exact size Show below will apply -- computed ahead of it so flip/clamp below sees the real footprint, not Vector2.Zero.
        var screenBounds = ScreenBoundsOverrideForTests ?? renderService.GraphicsDevice.Viewport.Bounds;
        var topLeft = _placedBelowAnchor
            ? PlaceBelow(_placementAnchor, menuSize, screenBounds)
            : PopupPositioning.GetPositionWithinBounds(_placementAnchor, menuSize, PopupAnchor.SouthEast, Vector2.Zero, screenBounds);
        _menu.Show(topLeft, options, _minimumWidth, _maximumHeight, markedIndex);
        _layers.OpenLightDismiss(this);
    }

    /// <summary>Left edges aligned under the anchor; above it instead when the screen has no room below and more above.</summary>
    private static Vector2 PlaceBelow(Rectangle anchor, Vector2 menuSize, Rectangle screenBounds)
    {
        var fitsBelow = anchor.Bottom + menuSize.Y <= screenBounds.Bottom;
        var roomAbove = anchor.Top - screenBounds.Top;
        var roomBelow = screenBounds.Bottom - anchor.Bottom;
        var y = fitsBelow || roomBelow >= roomAbove ? anchor.Bottom : anchor.Top - menuSize.Y;
        return Geometry.RectangleClamp.ClampInside(new Vector2(anchor.Left, y), menuSize, screenBounds);
    }

    public void Close()
    {
        _menu.Hide();
        Owner = null;
        _layers.CloseLightDismiss(this);
    }

    /// <summary>The menu and the element it was opened under are inside: a press on the owner closes what it opened rather than reopening it.</summary>
    public bool CountsAsInside(Element element) => element == _menu || element == Owner;

    public void Dismiss() => Close();

    /// <summary>Up/Down move the open dropdown's highlight and Enter chooses it. Returns whether the key was the dropdown's; nothing is when none is open.</summary>
    internal bool HandleNavigationKey(Keys key)
    {
        if (!IsOpen)
        {
            return false;
        }

        switch (key)
        {
            case Keys.Down:
                _menu.MoveHighlight(1);
                return true;
            case Keys.Up:
                _menu.MoveHighlight(-1);
                return true;
            case Keys.Enter:
                _menu.ChooseHighlighted();
                return true;
            default:
                return false;
        }
    }
}
