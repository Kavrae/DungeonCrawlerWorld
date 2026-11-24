using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>
/// Opens/closes the shared Dropdown popup and the submenus beside it -- the shared mechanics; what options appear is up
/// to the caller (a right-click's context menu through ContextMenuController, a DropdownField's choices). One list is
/// open at a time, with its open submenus: opening another replaces them all. UiInputController applies the framework
/// rules while one is open -- Escape closes the deepest menu, a press outside every menu closes them all, and
/// Up/Down/Enter/Left/Right reach the deepest menu before anything else (see HandleNavigationKey).
/// </summary>
/// <remarks>
/// The root list is positioned with the same PopupPositioning math Tooltip uses: at a point it is anchored SouthEast of
/// a zero-size rectangle there (its top-left pinned at the point, growing down and right, the native OS convention for a
/// context menu); below an element it is anchored to the element's bottom-left, flipping above it when there is no room
/// below. The root is one Dropdown kept for the controller's life. A submenu opens beside the row that chose it
/// (PlaceBeside), its parent staying open, as a Dropdown taken from the element pool and added to the layer last, so it
/// is drawn and hit above the menu that opened it. A closed submenu is hidden at once and returned to the pool when the
/// next menu opens, never inside the click that closed it (that click may still be running in one of its rows).
/// </remarks>
public sealed class DropdownController(ElementPoolService elementPoolService, UiRenderService renderService) : ILightDismissable
{
    private Dropdown _root = null!;

    /// <summary>The open menus, the root first and the deepest submenu last; empty while nothing is open.</summary>
    private readonly List<Dropdown> _openMenus = [];

    /// <summary>Submenus closed since the last open, hidden and waiting to go back to the pool.</summary>
    private readonly List<Dropdown> _closedSubmenus = [];

    private UiLayerStack _layers = null!;

    /// <summary>Where the root was placed.</summary>
    private Rectangle _placementAnchor;

    private bool _placedBelowAnchor;

    private float _minimumWidth;

    private float _maximumHeight = float.MaxValue;

    private bool _indentsUnderHeadings;

    /// <summary>What the open context menu's options act on, or null.</summary>
    private Element? _target;

    /// <summary>_target's PoolReturnCount when it became the target -- a different count is the instance's later use.</summary>
    private int _targetPoolReturnCount;

    public bool IsOpen => _openMenus.Count > 0 && _root.IsVisible;

    /// <summary>The root popup itself.</summary>
    internal Dropdown Menu => _root;

    /// <summary>Every open menu, the root first and the deepest submenu last.</summary>
    internal IReadOnlyList<Dropdown> OpenMenus => _openMenus;

    /// <summary>The element the open dropdown was opened under, or null for one opened at a point. A press on it is not an outside press, so the element can close what it opened rather than reopening it.</summary>
    public Element? Owner { get; private set; }

    /// <summary>
    /// Test-only override for the screen bounds opening clamps against -- lets a test drive the
    /// real option-list/click-routing logic without a real GraphicsDevice (unavailable
    /// headlessly). Production never sets this; opening falls back to the real Viewport when null.
    /// A property rather than a parameter, since opening is invoked deep inside the elements that open menus.
    /// </summary>
    internal Rectangle? ScreenBoundsOverrideForTests { get; set; }

    private Rectangle ScreenBounds => ScreenBoundsOverrideForTests ?? renderService.GraphicsDevice.Viewport.Bounds;

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _root = CreateMenu();
    }

    /// <summary>A hidden menu from the pool, wired to this controller and added to the layer above every menu before it.</summary>
    private Dropdown CreateMenu()
    {
        var menu = elementPoolService.CreateElement<Dropdown>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { IsVisible = false, MaximumSize = PopupChrome.DropdownMaximumSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        menu.Initialize();
        menu.SubmenuChosen = (optionIndex, submenu, fromKey) => OpenSubmenu(menu, optionIndex, submenu, fromKey);
        menu.OptionChosen = Close;
        menu.CloseSubmenus = () => CloseMenusBelow(menu);
        _layers.Add(UiLayer.ContextMenu, menu);
        return menu;
    }

    /// <summary>Opens options with the dropdown's top-left at point (flipped and clamped to stay on screen) -- a context menu -- for target, the element its options act on (null for none), indenting the options under each heading when indentsUnderHeadings.</summary>
    public void OpenAt(Vector2 point, IReadOnlyList<DropdownOption> options, Element? target = null, bool indentsUnderHeadings = false)
    {
        Owner = null;
        SetTarget(target);
        _placementAnchor = new Rectangle((int)point.X, (int)point.Y, 0, 0);
        _placedBelowAnchor = false;
        _minimumWidth = 0f;
        _maximumHeight = float.MaxValue;
        _indentsUnderHeadings = indentsUnderHeadings;
        PlaceRoot(options, markedIndex: -1);
    }

    /// <summary>Opens options under owner, at least as wide as it, scrolling past maximumHeight, with markedIndex's option marked and highlighted (-1 for none).</summary>
    public void OpenBelow(Element owner, IReadOnlyList<DropdownOption> options, int markedIndex = -1, float maximumHeight = float.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Owner = owner;
        SetTarget(null);
        _placementAnchor = owner.Rectangle;
        _placedBelowAnchor = true;
        _minimumWidth = owner.Rectangle.Width;
        _maximumHeight = maximumHeight;
        _indentsUnderHeadings = false;
        PlaceRoot(options, markedIndex);
    }

    /// <summary>Whether the open menu was opened for element, and element hasn't been closed since: what a target draws its highlight from.</summary>
    public bool IsOpenFor(Element element) => ReferenceEquals(Target, element);

    /// <summary>The element the open menu was opened for, while it hasn't been closed since; null when no menu with a live target is open.</summary>
    public Element? Target =>
        IsOpen && _target is not null && _target.PoolReturnCount == _targetPoolReturnCount ? _target : null;

    /// <summary>Makes target what the open menu acts on in place of the element it was opened for: the same thing shown by a new element (a grid that rebuilt its cells). Nothing when no menu with a target is open.</summary>
    public void Retarget(Element target)
    {
        if (IsOpen && _target is not null)
        {
            SetTarget(target);
        }
    }

    private void SetTarget(Element? target)
    {
        _target = target;
        _targetPoolReturnCount = target?.PoolReturnCount ?? 0;
    }

    /// <summary>Replaces the options of the list opened under owner on the next Update, keeping its place, scroll and highlight; nothing when no list of owner's is open.</summary>
    public void ReplaceOptions(Element owner, IReadOnlyList<DropdownOption> options)
    {
        if (IsOpen && ReferenceEquals(Owner, owner))
        {
            _root.ReplaceOptions(options);
        }
    }

    private void PlaceRoot(IReadOnlyList<DropdownOption> options, int markedIndex)
    {
        ReturnClosedSubmenus();
        CloseSubmenusFrom(1);

        // The menu's size comes from the rows it builds, so it is shown first and then moved where its real footprint fits.
        _root.Show(_root.RelativePosition, options, _minimumWidth, _maximumHeight, markedIndex, _indentsUnderHeadings);
        var menuSize = _root.CurrentSize;
        var screenBounds = ScreenBounds;
        var topLeft = _placedBelowAnchor
            ? PlaceBelow(_placementAnchor, menuSize, screenBounds)
            : PopupPositioning.GetPositionWithinBounds(_placementAnchor, menuSize, PopupAnchor.SouthEast, Vector2.Zero, screenBounds);
        _root.SetRelativePosition(topLeft);
        _openMenus.Clear();
        _openMenus.Add(_root);
        _layers.OpenLightDismiss(this);
    }

    /// <summary>Opens options beside the row at optionIndex of parent, closing whatever was open below parent; a submenu opened by a key highlights its first choosable option.</summary>
    private void OpenSubmenu(Dropdown parent, int optionIndex, IReadOnlyList<DropdownOption> options, bool fromKey)
    {
        ReturnClosedSubmenus();
        CloseMenusBelow(parent);

        var menu = CreateMenu();
        var screenBounds = ScreenBounds;
        var maximumRowsHeight = System.Math.Min(_maximumHeight, screenBounds.Height - menu.ContentInset.Y);
        menu.Show(menu.RelativePosition, options, maximumHeight: maximumRowsHeight, indentsUnderHeadings: _indentsUnderHeadings);
        var firstRowOffset = menu.ContentAbsolutePosition.Y - menu.AbsolutePosition.Y;
        var row = parent.OptionRows[optionIndex]!;
        menu.SetRelativePosition(PlaceBeside(parent.Rectangle, row.Rectangle.Top - firstRowOffset, menu.CurrentSize, screenBounds));
        if (fromKey)
        {
            menu.MoveHighlight(1);
        }

        parent.OpenSubmenuIndex = optionIndex;
        _openMenus.Add(menu);
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

    /// <summary>A submenu's top-left: against the parent's right edge at top, or against its left edge when the right would leave the screen; moved up, never flipped, when it would pass the bottom.</summary>
    internal static Vector2 PlaceBeside(Rectangle parent, float top, Vector2 menuSize, Rectangle screenBounds)
    {
        var x = parent.Right + menuSize.X <= screenBounds.Right ? parent.Right : parent.Left - menuSize.X;
        return Geometry.RectangleClamp.ClampInside(new Vector2(x, top), menuSize, screenBounds);
    }

    /// <summary>Closes every open menu below menu, leaving its highlight on the row that had opened the next; nothing when menu isn't open or has nothing open below it.</summary>
    private void CloseMenusBelow(Dropdown menu)
    {
        var level = _openMenus.IndexOf(menu);
        if (level < 0 || level == _openMenus.Count - 1)
        {
            return;
        }

        CloseSubmenusFrom(level + 1);
        menu.ReturnFromSubmenu();
    }

    /// <summary>Hides the open submenus from level down and sets them aside for the pool.</summary>
    private void CloseSubmenusFrom(int level)
    {
        for (var index = _openMenus.Count - 1; index >= level && index >= 1; index--)
        {
            var submenu = _openMenus[index];
            submenu.Hide();
            _closedSubmenus.Add(submenu);
            _openMenus.RemoveAt(index);
        }
    }

    /// <summary>Takes the submenus closed before this open out of the layer and back to the pool.</summary>
    private void ReturnClosedSubmenus()
    {
        foreach (var submenu in _closedSubmenus)
        {
            _layers.Remove(UiLayer.ContextMenu, submenu);
            elementPoolService.CloseElement(submenu);
        }

        _closedSubmenus.Clear();
    }

    /// <summary>Closes the deepest open submenu, or the whole list when no submenu is open.</summary>
    public void CloseDeepest()
    {
        if (_openMenus.Count > 1)
        {
            CloseMenusBelow(_openMenus[^2]);
            return;
        }

        Close();
    }

    public void Close()
    {
        CloseSubmenusFrom(1);
        _root.Hide();
        _openMenus.Clear();
        Owner = null;
        SetTarget(null);
        _layers.CloseLightDismiss(this);
    }

    /// <summary>Every open menu and the element the list was opened under are inside: a press on the owner closes what it opened rather than reopening it.</summary>
    public bool CountsAsInside(Element element)
    {
        if (element == Owner)
        {
            return true;
        }

        foreach (var menu in _openMenus)
        {
            if (element == menu)
            {
                return true;
            }
        }

        return false;
    }

    public void Dismiss() => Close();

    /// <summary>Up/Down move the deepest open menu's highlight, Home/End move it to the first or last option, and Enter chooses it; Right opens the highlighted option's submenu and Left closes the deepest submenu. Returns whether the key was the dropdown's; nothing is when none is open.</summary>
    /// <remarks>Enter with nothing highlighted in a list opened under an owner is the owner's (a searchable field resolves what was typed); a context menu keeps it. Left and Right are taken only when they open or close a submenu, so a field's text box under its list keeps them otherwise; Home and End are never taken from a text box owning the list.</remarks>
    internal bool HandleNavigationKey(Keys key)
    {
        if (!IsOpen)
        {
            return false;
        }

        var deepest = _openMenus[^1];
        switch (key)
        {
            case Keys.Down:
                deepest.MoveHighlight(1);
                return true;
            case Keys.Up:
                deepest.MoveHighlight(-1);
                return true;
            case Keys.Home when Owner is not TextBox:
                deepest.MoveHighlightToEnd(toLast: false);
                return true;
            case Keys.End when Owner is not TextBox:
                deepest.MoveHighlightToEnd(toLast: true);
                return true;
            case Keys.Enter:
                return deepest.ChooseHighlighted() || Owner is null;
            case Keys.Right:
                return deepest.OpenHighlightedSubmenu();
            case Keys.Left when _openMenus.Count > 1:
                CloseMenusBelow(_openMenus[^2]);
                return true;
            default:
                return false;
        }
    }
}
