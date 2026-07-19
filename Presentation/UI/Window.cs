using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.UI.ChromeBehaviors;

namespace Presentation.UI;

// TODO minimize and restore: child windows should hide/show and the parent should
// resize to title-bar-only when minimized. Opened/Closed/Resized/Moved below are the
// event-based mechanism intended to drive that.
// TODO recalculate tiled sibling windows on minimize and restore
// TODO persist selectionWindow child windows until selection changes
// TODO click-and-drag create a semi-transparent "ghost" window that follows the cursor.
public class Window
{
    public Guid WindowId { get; } = Guid.NewGuid();

    protected FontService FontService { get; }

    private readonly WindowService _windowService;

    /// <summary>Raised once the window has completed its initial setup.</summary>
    public event Action<Window>? Opened;

    /// <summary>Raised when the window is closed (before it's returned to WindowService's pool).</summary>
    public event Action<Window>? Closed;

    /// <summary>Raised when the window's current size actually changes.</summary>
    public event Action<Window>? Resized;

    /// <summary>Raised when the window's absolute screen position actually changes.</summary>
    public event Action<Window>? Moved;

    /// <summary>
    /// Raised whenever this window's content area is clicked, after OnContentClickAction
    /// (so subclass overrides like TextWindow's or MapWindow's own click handling still run
    /// first). Fired from HandleContentClick directly, not from the virtual
    /// OnContentClickAction, so subclasses that override it without calling base still raise
    /// this -- letting external code (e.g. NotificationCenter) react to a click without
    /// needing a dedicated Window subclass just to hook one in.
    /// </summary>
    public event Action<Window>? Clicked;

    /// <summary>
    /// Raised whenever this window's WindowDisplayMode actually changes, regardless of what
    /// triggered it -- not just its own chrome buttons. Lets external code (e.g. a future
    /// "minimize all" action, or a chrome behavior reacting to a mode it didn't itself set)
    /// react to the change without needing to be the one that called SetWindowDisplayMode.
    /// </summary>
    public event Action<Window>? DisplayModeChanged;

    /*========Window hierarchy========*/
    protected Window? _parentWindow;
    public Window? ParentWindow => _parentWindow;

    protected bool _canContainChildWindows;
    public bool CanContainChildWindows => _canContainChildWindows;

    protected WindowTileMode _childWindowTileMode;
    public WindowTileMode ChildWindowTileMode => _childWindowTileMode;

    protected List<Window> _childWindows = [];
    public List<Window> ChildWindows => _childWindows;

    private IWindowContent? _content;

    /*========Window========*/
    protected WindowDisplayMode _windowDisplayMode;
    public WindowDisplayMode WindowDisplay => _windowDisplayMode;

    protected WindowDisplayMode _previousWindowDisplayMode;
    public WindowDisplayMode PreviousWindowDisplay => _previousWindowDisplayMode;

    protected Vector2 _windowAbsolutePosition;
    public Vector2 WindowAbsolutePosition => _windowAbsolutePosition;

    /// <summary>Position relative to the parent window.</summary>
    protected Vector2 _windowRelativePosition;
    public Vector2 WindowRelativePosition => _windowRelativePosition;

    protected Vector2 _windowOriginalSize;
    public Vector2 WindowOriginalSize => _windowOriginalSize;

    protected Vector2 _windowCurrentSize;
    public Vector2 WindowCurrentSize => _windowCurrentSize;

    protected Vector2 _windowMinimumSize;
    public Vector2 WindowMinimumSize => _windowMinimumSize;

    protected Vector2 _windowMaximumSize;
    public Vector2 WindowMaximumSize => _windowMaximumSize;

    protected Rectangle _windowRectangle;
    public Rectangle WindowRectangle => _windowRectangle;

    protected bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    protected bool _isTransparent;
    public bool IsTransparent => _isTransparent;

    /*========Title========*/
    /// <summary>
    /// Internal, not protected: chrome behaviors (see IWindowChromeBehavior) live outside
    /// the Window subclass hierarchy but still need the window's title font to build
    /// matching title buttons.
    /// </summary>
    internal SpriteFontBase TitleFont { get; }

    protected bool _showTitle;
    public bool ShowTitle => _showTitle;

    protected bool _showTitleWhenMinimized;
    public bool ShowTitleWhenMinimized => _showTitleWhenMinimized;

    protected string _titleText = string.Empty;
    public string TitleText { get => _titleText; set => _titleText = value; }
    public Vector2 TitlePadding = new(5, 2);

    protected Vector2 _originalTitleSize;
    public Vector2 OriginalTitleSize => _originalTitleSize;

    protected Vector2 _titleSize;
    public Vector2 TitleSize => _titleSize;

    protected Vector2 _titleAbsolutePosition;
    public Vector2 TitleAbsolutePosition => _titleAbsolutePosition;

    protected Rectangle _titleRectangle;
    public Rectangle TitleRectangle => _titleRectangle;

    protected Color _titleBackgroundColor;
    public Color TitleColor => _titleBackgroundColor;

    protected List<Button> _titleButtons = [];
    public List<Button> TitleButtons { get => _titleButtons; set => _titleButtons = value; }

    /*========Border========*/
    protected bool _showBorder;
    public bool ShowBorder => _showBorder;

    protected Vector2 _borderSize;
    public Vector2 BorderSize => _borderSize;

    /*========Content========*/
    protected Vector2 _contentAbsolutePosition;
    public Vector2 ContentAbsolutePosition => _contentAbsolutePosition;

    protected Vector2 _contentSize;
    public Vector2 ContentSize => _contentSize;

    protected Rectangle _contentRectangle;
    public Rectangle ContentRectangle => _contentRectangle;

    public Vector2 ContentPadding = new(5, 5);

    protected Color _contentBackgroundColor;
    public Color ContentColor => _contentBackgroundColor;

    /*========Viewport========*/
    private Viewport _windowViewport;
    public Viewport WindowViewport => _windowViewport;

    private Matrix _cameraTransform;
    public Matrix CameraTransform => _cameraTransform;

    /*========User Controls========*/
    public bool CanUserClose { get; set; }
    public bool CanUserMinimize { get; set; }
    public bool CanUserMove { get; set; }
    public bool CanUserResize { get; set; }
    public bool CanUserScrollHorizontal { get; set; }
    public bool CanUserScrollVertical { get; set; }

    public Window(FontService fontService, WindowService windowService)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(windowService);

        FontService = fontService;
        TitleFont = fontService.GetFont(8);
        _windowService = windowService;
    }

    public virtual void BuildWindow(Window? parentWindow, WindowOptions windowOptions)
    {
        ArgumentNullException.ThrowIfNull(windowOptions);

        var hierarchy = windowOptions.Hierarchy;
        var layout = windowOptions.Layout;
        var chrome = windowOptions.Chrome;
        var content = windowOptions.Content;

        /*========Window hierarchy========*/
        _parentWindow = parentWindow;
        _canContainChildWindows = hierarchy?.CanContainChildWindows ?? false;
        _childWindowTileMode = hierarchy?.ChildWindowTileMode ?? WindowTileMode.Floating;
        _childWindows = [];

        /*========Window========*/
        _windowDisplayMode = layout?.DisplayMode ?? WindowDisplayMode.Static;
        _windowRelativePosition = layout?.RelativePosition ?? new Vector2();
        _windowAbsolutePosition = _parentWindow != null
            ? _parentWindow.ContentAbsolutePosition + _windowRelativePosition
            : _windowRelativePosition;

        _windowMinimumSize = layout?.MinimumSize ?? new Vector2(0, 0);
        _windowMaximumSize = layout?.MaximumSize ?? _parentWindow?.ContentSize ?? layout?.Size ?? new Vector2(0, 0);
        _windowOriginalSize = layout?.Size ?? new Vector2(0, 0);
        _windowCurrentSize = _windowOriginalSize;

        _isVisible = layout?.IsVisible ?? true;
        _isTransparent = layout?.IsTransparent ?? false;

        /*========Title========*/
        _showTitle = chrome?.ShowTitle ?? false;
        _showTitleWhenMinimized = chrome?.ShowTitleWhenMinimized ?? false;
        _titleText = chrome?.TitleText ?? string.Empty;
        _originalTitleSize = new Vector2(_windowOriginalSize.X, TitleFont.MeasureString(" ").Y + TitlePadding.Y * 3);
        _titleSize = _originalTitleSize;
        _titleBackgroundColor = chrome?.TitleColor ?? Color.LightBlue;
        _titleButtons = [];

        /*========Border========*/
        _showBorder = chrome?.ShowBorder ?? false;
        _borderSize = chrome?.BorderSize ?? new Vector2(1, 1);

        /*========Content========*/
        _contentBackgroundColor = content?.ContentColor ?? Color.White;

        /*========User Controls========*/
        CanUserClose = chrome?.CanUserClose ?? false;
        CanUserMinimize = chrome?.CanUserMinimize ?? false;
        CanUserMove = chrome?.CanUserMove ?? false;
        CanUserResize = chrome?.CanUserResize ?? false;
        CanUserScrollHorizontal = chrome?.CanUserScrollHorizontal ?? false;
        CanUserScrollVertical = chrome?.CanUserScrollVertical ?? false;
    }

    public virtual void Initialize()
    {
        RecalculateSizeAndAbsolutePosition();

        _windowViewport = new Viewport(_contentRectangle);
        _cameraTransform = Matrix.CreateRotationZ(0) * // camera rotation, default 0
                           Matrix.CreateScale(new Vector3(1, 1, 1)); // TODO zoom

        if (_showTitle)
        {
            // Close/minimize/restore are the standard, near-universal chrome capabilities,
            // so Window still decides whether to attach them from the existing options
            // flags. Anything else -- including move/resize/dock once built -- attaches
            // via AddChromeBehavior from outside Window, which never needs to know they exist.
            // Close is attached first (and so ends up rightmost, per AddTitleButton's
            // right-to-left insertion order) so the standard layout is always
            // [minimize/restore] [close] with both grouped on the title bar's right side.
            if (CanUserClose)
            {
                AddChromeBehavior(new WindowCloseBehavior());
            }

            if (CanUserMinimize)
            {
                AddChromeBehavior(new WindowMinimizeRestoreBehavior());
            }
        }

        foreach (var button in _titleButtons)
        {
            button.Initialize();
        }

        foreach (var childWindow in _childWindows)
        {
            childWindow.Initialize();
        }

        // Runs after the loop above (not before): content that adds its own child windows
        // (e.g. SelectionWindowContent) does so via AddChildWindow, which already
        // initializes each window it adds -- running content.Initialize() first would let
        // its children get caught by the loop above and initialized a second time.
        _content?.Initialize(this);

        Opened?.Invoke(this);
    }

    public virtual void LoadContent()
    {
        foreach (var childWindow in _childWindows)
        {
            childWindow.LoadContent();
        }
    }

    public virtual void Update(GameTime gameTime)
    {
        foreach (var button in _titleButtons)
        {
            button.Update(gameTime);
        }

        _content?.Update(gameTime);

        foreach (var childWindow in _childWindows)
        {
            childWindow.Update(gameTime);
        }
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_isVisible)
        {
            return;
        }

        // TODO redo to put border around title and content separately in all display modes
        if (_showBorder)
        {
            spriteBatch.Draw(unitRectangle, _windowRectangle, Color.Black);
        }

        if ((_windowDisplayMode != WindowDisplayMode.Minimized && _showTitle) || (_windowDisplayMode == WindowDisplayMode.Minimized && _showTitleWhenMinimized))
        {
            if (!_isTransparent)
            {
                spriteBatch.Draw(unitRectangle, _titleRectangle, _titleBackgroundColor);
            }
            spriteBatch.DrawString(TitleFont, TitleText, _titleAbsolutePosition + TitlePadding, Color.Black);

            foreach (var button in _titleButtons)
            {
                button.Draw(gameTime, spriteBatch, unitRectangle);
            }
        }

        if (_windowDisplayMode != WindowDisplayMode.Minimized)
        {
            if (!_isTransparent)
            {
                spriteBatch.Draw(unitRectangle, _contentRectangle, _contentBackgroundColor);
            }

            spriteBatch.End();

            // Creating a separate viewport for the content allows windows to be individually scrolled and clipped.
            var previousViewport = graphicsDevice.Viewport;
            graphicsDevice.Viewport = WindowViewport;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, CameraTransform);

            DrawContent(gameTime, spriteBatch, unitRectangle);

            spriteBatch.End();
            graphicsDevice.Viewport = previousViewport;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            foreach (var childWindow in _childWindows)
            {
                childWindow.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
            }
        }
    }

    public virtual void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        _content?.DrawContent(gameTime, spriteBatch, unitRectangle);
    }

    public bool IsInDisplayRectangle(int x, int y) => _contentRectangle.Contains(x, y);

    public void HandleClick(Point mousePosition)
    {
        // _titleRectangle is sized/positioned from _titleSize/_titleAbsolutePosition
        // unconditionally (RecalculateRectangles doesn't know about _showTitle), so without
        // this guard a titleless window's upper region is a dead zone: clicks there land in
        // an invisible title rect and route to HandleTitleClick (which only checks title
        // buttons, of which there are none) instead of falling through to content.
        if (_showTitle && _titleRectangle.Contains(mousePosition))
        {
            HandleTitleClick(mousePosition);
        }
        else if (_contentRectangle.Contains(mousePosition))
        {
            HandleContentClick(mousePosition);
        }
    }

    private void HandleTitleClick(Point mousePosition) => OnTitleClickAction(mousePosition);

    protected virtual void OnTitleClickAction(Point mousePosition)
    {
        foreach (var button in _titleButtons)
        {
            if (button.ButtonRectangle.Contains(mousePosition))
            {
                button.HandleClick(mousePosition);
            }
        }
    }

    private void HandleContentClick(Point mousePosition)
    {
        OnContentClickAction(mousePosition);
        Clicked?.Invoke(this);
    }

    protected virtual void OnContentClickAction(Point mousePosition)
    {
        foreach (var childWindow in _childWindows)
        {
            if (childWindow.WindowRectangle.Contains(mousePosition))
            {
                childWindow.HandleClick(mousePosition);
            }
        }
    }

    public void AddTitleButton(Button newButton, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(newButton);

        if (!_showTitle)
        {
            return;
        }

        var maximumIndex = _titleButtons.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _titleButtons.Insert(clampedInsertIndex, newButton);

        RepositionTitleButtons();
    }

    /// <summary>
    /// Right-aligns title buttons against the current title width, tiling each earlier-added
    /// button further left of the one after it -- re-run on every recalculation (not just
    /// once at attach time), since minimizing shrinks the title bar to fit just its text.
    /// Without this, a button's cached relative position (computed against the window's full
    /// static width) would drift outside the shrunk title bar once minimized, making it
    /// unclickable exactly when it's needed to restore the window.
    /// </summary>
    private void RepositionTitleButtons()
    {
        for (var index = 0; index < _titleButtons.Count; index++)
        {
            var button = _titleButtons[index];
            if (index == 0)
            {
                button.ChangeRelativePosition(new Vector2(_titleSize.X - button.Size.X - 3, 3));
            }
            else
            {
                var previousButton = _titleButtons[index - 1];
                button.ChangeRelativePosition(new Vector2(
                    previousButton.RelativePosition.X - previousButton.Size.X - 3,
                    previousButton.RelativePosition.Y));
            }
        }
    }

    /// <summary>Attaches a chrome capability (see IWindowChromeBehavior) to this window.</summary>
    public void AddChromeBehavior(IWindowChromeBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);

        behavior.Attach(this);
    }

    /// <summary>
    /// Attaches what this window draws in its content area (see IWindowContent), instead of
    /// subclassing Window and overriding DrawContent. Must be called before Initialize --
    /// content's own Initialize(this) runs as part of Window.Initialize().
    /// </summary>
    public void SetContent(IWindowContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
    }

    public void AddChildWindow(Window newChildWindow, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(newChildWindow);

        if (!_canContainChildWindows)
        {
            return;
        }

        var maximumIndex = _childWindows.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _childWindows.Insert(clampedInsertIndex, newChildWindow);

        for (var updateIndex = clampedInsertIndex; updateIndex < _childWindows.Count; updateIndex++)
        {
            if (_childWindowTileMode == WindowTileMode.Floating)
            {
                // Let the window's creator determine its relative position and draw order.
            }
            else if (updateIndex == 0)
            {
                newChildWindow._windowRelativePosition = new Vector2(0, 0);
            }
            else
            {
                var previousChildWindow = _childWindows[updateIndex - 1];
                if (_childWindowTileMode == WindowTileMode.Horizontal)
                {
                    newChildWindow._windowRelativePosition = new Vector2(
                        previousChildWindow._windowRelativePosition.X + previousChildWindow._windowCurrentSize.X,
                        previousChildWindow._windowRelativePosition.Y);
                }
                else if (_childWindowTileMode == WindowTileMode.Vertical)
                {
                    newChildWindow._windowRelativePosition = new Vector2(
                        previousChildWindow._windowRelativePosition.X,
                        previousChildWindow._windowRelativePosition.Y + previousChildWindow._windowCurrentSize.Y);
                }
            }

            newChildWindow.Initialize();
        }
    }

    /// <summary>Empty and re-add so sibling positions recalculate around the removed window.</summary>
    public void RemoveChildWindow(Guid windowId)
    {
        var childWindowIndex = _childWindows.FindIndex(childWindow => childWindow.WindowId == windowId);
        if (childWindowIndex < 0)
        {
            return;
        }

        _childWindows.RemoveAt(childWindowIndex);
        for (var index = childWindowIndex; index < _childWindows.Count; index++)
        {
            _childWindows[index].Initialize();
        }
    }

    public void RecalculateSizeAndAbsolutePosition()
    {
        // If there is no parent, this is a root window guaranteed to have a maximum set.
        if (_parentWindow != null)
        {
            _windowMaximumSize = _parentWindow.ContentSize - _windowRelativePosition;
        }

        var previousSize = _windowCurrentSize;

        switch (_windowDisplayMode)
        {
            case WindowDisplayMode.Minimized:
                RecalculateMinimizedWindowSize();
                break;
            case WindowDisplayMode.Static:
                RecalculateStaticWindowSize();
                break;
            case WindowDisplayMode.Fill:
                RecalculateFillWindowSize();
                break;
            case WindowDisplayMode.Grow:
                RecalculateGrowWindowSize();
                break;
            default:
                throw new NotImplementedException("No default window display mode.");
        }

        RecalculateAbsolutePositions();
        RecalculateRectangles();
        RecalculateButtonsSizeAndPosition();
        RecalculateChildrenSizeAndPosition();

        if (_windowCurrentSize != previousSize)
        {
            Resized?.Invoke(this);
        }
    }

    public void RecalculateAbsolutePositions()
    {
        var previousAbsolutePosition = _windowAbsolutePosition;

        _windowAbsolutePosition = _parentWindow != null
            ? _parentWindow.WindowAbsolutePosition + _windowRelativePosition
            : _windowRelativePosition;

        _titleAbsolutePosition = new Vector2(
            _windowAbsolutePosition.X + (_showBorder ? _borderSize.X : 0),
            _windowAbsolutePosition.Y + (_showBorder ? _borderSize.Y : 0));

        _contentAbsolutePosition = new Vector2(
            _windowAbsolutePosition.X + (_showBorder ? _borderSize.X : 0),
            _windowAbsolutePosition.Y + (_showBorder ? _borderSize.Y : 0) + (_showTitle ? _titleSize.Y : 0));

        if (_windowAbsolutePosition != previousAbsolutePosition)
        {
            Moved?.Invoke(this);
        }
    }

    public virtual void RecalculateMinimizedWindowSize()
    {
        var textSize = TitleFont.MeasureString(_titleText);
        _titleSize = new Vector2(textSize.X + TitlePadding.X * 2, textSize.Y + TitlePadding.Y * 2);

        _contentSize = new Vector2(0, 0);

        var windowSize = new Vector2(
            _titleSize.X + (_showBorder ? _borderSize.X * 2 : 0),
            _titleSize.Y + (_showBorder ? _borderSize.Y * 2 : 0));

        _windowCurrentSize = new Vector2(
            MathHelper.Clamp(windowSize.X, _windowMinimumSize.X, _windowMaximumSize.X),
            windowSize.Y);
    }

    public virtual void RecalculateStaticWindowSize()
    {
        _windowCurrentSize = new Vector2(
            MathHelper.Clamp(_windowOriginalSize.X, _windowMinimumSize.X, _windowMaximumSize.X),
            MathHelper.Clamp(_windowOriginalSize.Y, _windowMinimumSize.Y, _windowMaximumSize.Y));

        // Resize horizontally to fit the new window size, but keep the vertical size.
        _titleSize = new Vector2(
            _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
            _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));

        _contentSize = new Vector2(
            _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
            _windowCurrentSize.Y - (_showBorder ? _borderSize.Y : 0) - (_showTitle ? _titleSize.Y : 0));
    }

    public virtual void RecalculateFillWindowSize()
    {
        _windowCurrentSize = _windowMaximumSize;

        _titleSize = new Vector2(
            _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
            _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));

        _contentSize = _windowCurrentSize
            - (_showTitle ? _titleSize : new Vector2(0, 0))
            - (_showBorder ? Vector2.Multiply(_borderSize, 2) : new Vector2(0, 0));
    }

    public virtual void RecalculateGrowWindowSize()
    {
        if (_canContainChildWindows && _childWindows.Count > 0)
        {
            var maxRight = 0;
            var maxBottom = 0;
            foreach (var childWindow in _childWindows)
            {
                maxRight = System.Math.Max(maxRight, childWindow.WindowRectangle.Right);
                maxBottom = System.Math.Max(maxBottom, childWindow.WindowRectangle.Bottom);
            }
            _contentSize = new Vector2(maxRight, maxBottom);
        }
        else
        {
            _contentSize = new Vector2(0, 0);
        }

        _contentSize += ContentPadding;

        _windowCurrentSize = _contentSize;
        if (_showTitle)
        {
            _titleSize = new Vector2(_contentSize.X, _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));
            _windowCurrentSize += _titleSize;
        }
        if (_showBorder)
        {
            _windowCurrentSize += Vector2.Multiply(_borderSize, 2);
        }
    }

    /// <summary>Recalculates draw rectangles from the current absolute positions/sizes.</summary>
    public void RecalculateRectangles()
    {
        _windowRectangle = new Rectangle((int)_windowAbsolutePosition.X, (int)_windowAbsolutePosition.Y, (int)_windowCurrentSize.X, (int)_windowCurrentSize.Y);
        _titleRectangle = new Rectangle((int)_titleAbsolutePosition.X, (int)_titleAbsolutePosition.Y, (int)_titleSize.X, (int)_titleSize.Y);
        _contentRectangle = new Rectangle((int)_contentAbsolutePosition.X, (int)_contentAbsolutePosition.Y, (int)_contentSize.X, (int)_contentSize.Y);
    }

    /// <summary>
    /// Re-tiles every title button against the current title width (see
    /// RepositionTitleButtons) rather than just refreshing each button's absolute position
    /// from its previously-computed relative one, since the title width itself can change
    /// (e.g. minimizing shrinks it to fit just the title text).
    /// </summary>
    public void RecalculateButtonsSizeAndPosition()
    {
        RepositionTitleButtons();
    }

    public void RecalculateChildrenSizeAndPosition()
    {
        foreach (var childWindow in _childWindows)
        {
            childWindow.RecalculateSizeAndAbsolutePosition();
        }
    }

    public void SetIsVisible(bool isVisible)
    {
        _isVisible = isVisible;
        _parentWindow?.RecalculateChildrenSizeAndPosition();
    }

    public void SetWindowDisplayMode(WindowDisplayMode newWindowDisplayMode)
    {
        // No-op guard matters beyond just skipping redundant work: without it, a call with
        // the mode the window is already in would still overwrite PreviousWindowDisplay with
        // that same current mode, corrupting what a later restore should return to.
        if (newWindowDisplayMode == _windowDisplayMode)
        {
            return;
        }

        _previousWindowDisplayMode = _windowDisplayMode;
        _windowDisplayMode = newWindowDisplayMode;
        RecalculateSizeAndAbsolutePosition();
        DisplayModeChanged?.Invoke(this);
    }

    /// <summary>
    /// Repositions the window relative to its parent (or the screen, for a root window).
    /// For chrome behaviors that need to move a window (e.g. a future drag-to-move) --
    /// Resized/Moved fire automatically through RecalculateSizeAndAbsolutePosition.
    /// </summary>
    public void SetRelativePosition(Vector2 relativePosition)
    {
        _windowRelativePosition = relativePosition;
        RecalculateSizeAndAbsolutePosition();
    }

    /// <summary>
    /// Sets the window's Static-mode size. Only display mode Static reads this size --
    /// Fill/Grow compute size from the parent/content instead, so this method has no
    /// visible effect on a Fill or Grow window, matching that a window can't be manually
    /// resized while it's set to auto-size.
    /// </summary>
    public void SetSize(Vector2 size)
    {
        _windowOriginalSize = size;
        RecalculateSizeAndAbsolutePosition();
    }

    public void Close()
    {
        Closed?.Invoke(this);
        _windowService.CloseWindow(this);
    }
}
