using Engine.Utilities;
using Microsoft.Xna.Framework;
using UIEngine.Geometry;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Details;

/// <summary>A details popup's window: what its controller hooks into every update.</summary>
public interface IDetailsPopupWindow
{
    /// <summary>Called at the end of every Update -- the controller's placement and close-on-leave check.</summary>
    Action? OnUpdated { get; set; }
}

/// <summary>The timing every details popup shares.</summary>
public static class DetailsPopupTiming
{
    /// <summary>How long the pointer may be away from both what is shown and the popup before it closes -- time to cross the gap the cursor offset leaves.</summary>
    public static readonly int PointerAwayGraceFrames = GameTiming.FramesForSeconds(0.3f);
}

/// <summary>One details popup's lifecycle -- Item Details', Action Details' and their comparison popups': one thing shown at a time, opened just below and right of the cursor, closed when the pointer has left both what it shows and the popup for a moment -- unless pinned.</summary>
/// <remarks>
/// Showing something else while it is open updates it in place and moves it to the cursor (or to FixedPosition). It stays
/// open while the pointer is over what it shows (IsPointerOverShown, the subclass's rule), over the popup itself or over a
/// context menu; otherwise it closes once the pointer has been away for DetailsPopupTiming.PointerAwayGraceFrames. Pinned
/// (Compare), it moves beside the window holding what it shows and stays wherever the pointer goes. With
/// closesOnPressOutside, a press outside every window of popupGroup closes it (light dismiss). Without
/// closesWhenPointerLeaves (a comparison popup, kept below the pinned one) it stays open until it shows something else or
/// is closed. Its width is the width source window's; it is a menu window in DynamicHud, a member of popupGroup.
/// </remarks>
public abstract class DetailsPopupController<TWindow>(
    ElementPoolService elementPoolService,
    PointerState pointerState,
    ElementGroup popupGroup,
    Func<Vector2> screenSize,
    bool closesOnPressOutside,
    bool closesWhenPointerLeaves) : ILightDismissable
    where TWindow : Window, IDetailsPopupWindow
{
    /// <summary>How far below and right of the cursor the popup's top-left lands, so a double-click on what is shown never lands on the popup's edge.</summary>
    private static readonly Vector2 CursorOffset = new(16, 16);

    private UiLayerStack _layers = null!;
    private TWindow? _window;
    private int _pointerAwayFrames;

    public bool IsOpen => _window is not null;

    /// <summary>True from PinBeside until the window closes: it stays where it was put, wherever the pointer goes.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>The open window's own bounds -- Rectangle.Empty when nothing is open.</summary>
    public Rectangle Rectangle => _window?.Rectangle ?? Rectangle.Empty;

    /// <summary>The open window, if any.</summary>
    public TWindow? Window => _window;

    /// <summary>Settable late-bound notification fired when the window closes.</summary>
    public Action? OnClosed { get; set; }

    /// <summary>Settable late-bound: where the popup's top-left goes, kept there every update, instead of beside the cursor -- a comparison popup's, below the pinned one.</summary>
    public Func<Vector2>? FixedPosition { get; set; }

    public void Initialize(UiLayerStack layers) => _layers = layers;

    /// <summary>Every window of popupGroup is inside -- a press anywhere else closes the popup and goes no further.</summary>
    public bool CountsAsInside(Element element) => popupGroup.Contains(element);

    public void Dismiss() => Close();

    public void Close() => _window?.Close();

    /// <summary>Pins the open window beside besideWindow -- to its right, else its left, wherever it overlaps no other window of popupGroup and stays on screen -- so it stays put, whatever the pointer does, until it closes.</summary>
    public void PinBeside(Window besideWindow)
    {
        if (_window is not { } window)
        {
            return;
        }

        IsPinned = true;
        var size = window.CurrentSize;
        var screen = screenSize();
        var beside = besideWindow.Rectangle;
        Vector2[] candidates =
        [
            new(beside.Right + WindowCascadePlacement.Gap, beside.Top),
            new(beside.Left - WindowCascadePlacement.Gap - size.X, beside.Top),
        ];

        foreach (var candidate in candidates)
        {
            var clamped = new Vector2(candidate.X, RectangleClamp.ClampInside(candidate, size, screen).Y);
            if (clamped.X >= 0 && clamped.X + size.X <= screen.X && !OverlapsAnotherGroupWindow(window, clamped, size))
            {
                window.SetRelativePosition(clamped);
                return;
            }
        }

        PlaceAt(window, candidates[0]);
    }

    /// <summary>Shows what configure puts in the window, at the cursor (or FixedPosition), its width widthSourceWindow's: the open window updated in place, else a new one, which wire hooks up before it initializes.</summary>
    protected TWindow Show(Window widthSourceWindow, Action<TWindow> configure, Action<TWindow> wire)
    {
        var openPosition = FixedPosition?.Invoke() ?? new Vector2(pointerState.CursorPosition.X, pointerState.CursorPosition.Y) + CursorOffset;
        _pointerAwayFrames = 0;

        if (_window is { } existing)
        {
            configure(existing);
            PlaceAt(existing, openPosition);
            return existing;
        }

        var window = elementPoolService.CreateElement<TWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = openPosition,
                MaximumSize = new Vector2(widthSourceWindow.CurrentSize.X, screenSize().Y),
                DisplayMode = ElementDisplayMode.WrapContent,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = false,
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = false,
                CanUserResize = false,
                CanUserScrollVertical = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        configure(window);
        window.Closed += HandleClosed;
        window.OnUpdated = HandleWindowUpdated;
        wire(window);
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        _layers.OpenMenuWindow(window);
        popupGroup.Join(window);
        if (closesOnPressOutside)
        {
            _layers.OpenLightDismiss(this);
        }

        _window = window;
        PlaceAt(window, openPosition);
        return window;
    }

    /// <summary>Whether element -- the hovered element or one of its ancestors -- shows what the popup shows.</summary>
    protected abstract bool IsPointerOverShown(Element element);

    /// <summary>The popup closed: forget what it showed. OnClosed is invoked after.</summary>
    protected abstract void OnWindowClosed();

    private bool OverlapsAnotherGroupWindow(Window window, Vector2 position, Vector2 size)
    {
        var bounds = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
        foreach (var member in popupGroup.Members)
        {
            if (!ReferenceEquals(member, window) && member.IsVisible && member.Rectangle.Intersects(bounds))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts window's top-left at position, moved only as far as it takes to keep it on screen.</summary>
    private void PlaceAt(Window window, Vector2 position)
    {
        var placed = RectangleClamp.ClampInside(position, window.CurrentSize, screenSize());
        if (placed != window.RelativePosition)
        {
            window.SetRelativePosition(placed);
        }
    }

    /// <summary>Each update of the open window: keep it on screen as its content grows, and -- unless pinned -- close it once the pointer has left both what it shows and the window.</summary>
    private void HandleWindowUpdated()
    {
        if (_window is not { } window)
        {
            return;
        }

        PlaceAt(window, FixedPosition?.Invoke() ?? window.RelativePosition);

        if (IsPinned || !closesWhenPointerLeaves || IsPointerOverPopupOrShown(window))
        {
            _pointerAwayFrames = 0;
            return;
        }

        _pointerAwayFrames++;
        if (_pointerAwayFrames > DetailsPopupTiming.PointerAwayGraceFrames)
        {
            Close();
        }
    }

    private bool IsPointerOverPopupOrShown(TWindow window)
    {
        for (var element = pointerState.HoveredElement; element is not null; element = element.ParentElement)
        {
            if (ReferenceEquals(element, window) || element is Dropdown || IsPointerOverShown(element))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleClosed(Element closedWindow)
    {
        _layers.Remove(UiLayer.DynamicHud, closedWindow);
        _layers.CloseMenuWindow(closedWindow);
        popupGroup.Leave(closedWindow);
        if (closesOnPressOutside)
        {
            _layers.CloseLightDismiss(this);
        }

        _window = null;
        IsPinned = false;
        OnWindowClosed();
        OnClosed?.Invoke();
    }
}
