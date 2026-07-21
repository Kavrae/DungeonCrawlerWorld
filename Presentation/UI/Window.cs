using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
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

    /// <summary>
    /// Raised when the window's current size actually changes. Fires during the Measure
    /// pass, which runs across the whole subtree before Arrange (see Measure/Arrange) --
    /// so every node's Resized fires before any node's Moved, not interleaved node-by-node
    /// the way they were before the two-pass split.
    /// </summary>
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
    /// <summary>
    /// Position/size/display-mode bookkeeping grouped into one object -- see
    /// WindowGeometryState -- rather than several independent fields, so a future
    /// WindowMoveBehavior/WindowResizeBehavior (attached the same way WindowCloseBehavior is)
    /// has a single cohesive surface to read/mutate instead of reaching into many.
    /// </summary>
    private protected readonly WindowGeometryState _geometry = new();

    public WindowDisplayMode WindowDisplay => _geometry.DisplayMode;
    public WindowDisplayMode PreviousWindowDisplay => _geometry.PreviousDisplayMode;
    public Vector2 WindowAbsolutePosition => _geometry.AbsolutePosition;
    public Vector2 WindowRelativePosition => _geometry.RelativePosition;
    public Vector2 WindowOriginalSize => _geometry.OriginalSize;
    public Vector2 WindowCurrentSize => _geometry.CurrentSize;
    public Vector2 WindowMinimumSize => _geometry.MinimumSize;
    public Vector2 WindowMaximumSize => _geometry.MaximumSize;
    public Rectangle WindowRectangle => _geometry.Rectangle;

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

    /// <summary>Internal, not protected, for the same reason as TitleFont -- Button uses this to center its label the same way GlyphRenderer centers map tile glyphs.</summary>
    internal GlyphRenderer GlyphRenderer { get; }

    /// <summary>Title bar bookkeeping -- see WindowGeometryState's doc comment for the same "grouped, plain fields" rationale.</summary>
    private protected readonly WindowTitleState _title = new();

    public bool ShowTitle => _title.ShowTitle;
    public bool ShowTitleWhenMinimized => _title.ShowWhenMinimized;
    public string TitleText { get => _title.Text; set => _title.Text = value; }
    public Vector2 TitlePadding { get; set; } = new(5, 2);
    public Vector2 OriginalTitleSize => _title.OriginalSize;
    public Vector2 TitleSize => _title.Size;
    public Vector2 TitleAbsolutePosition => _title.AbsolutePosition;
    public Rectangle TitleRectangle => _title.Rectangle;

    /// <summary>Title bar height if shown, else zero -- see BorderInset for the analogous border helper.</summary>
    protected float TitleInsetHeight => _title.ShowTitle ? _title.Size.Y : 0;

    public Color TitleColor => _title.BackgroundColor;
    public List<Button> TitleButtons { get => _title.Buttons; set => _title.Buttons = value; }

    /*========Border========*/
    /// <summary>Border bookkeeping -- see WindowGeometryState's doc comment for the same "grouped, plain fields" rationale.</summary>
    private protected readonly WindowBorderState _border = new();

    public bool ShowBorder => _border.Show;

    /// <summary>
    /// Border thickness on one edge if the border is shown, else zero -- single source of
    /// truth for how much border eats into title/content space, replacing what used to be an
    /// independently-repeated `_showBorder ? _borderSize.X : 0`-style ternary in every
    /// RecalculateXxxWindowSize method and RecalculateAbsolutePositions.
    /// </summary>
    protected Vector2 BorderInset => _border.Show ? new Vector2(_border.Thickness.Left, _border.Thickness.Top) : Vector2.Zero;

    /// <summary>Border thickness on both edges of an axis (e.g. left+right) if shown, else zero.</summary>
    protected Vector2 BorderInsetDoubled => _border.Show ? new Vector2(_border.Thickness.Horizontal, _border.Thickness.Vertical) : Vector2.Zero;

    public Rectangle BorderTopRectangle => _border.TopRectangle;
    public Rectangle BorderBottomRectangle => _border.BottomRectangle;
    public Rectangle BorderLeftRectangle => _border.LeftRectangle;
    public Rectangle BorderRightRectangle => _border.RightRectangle;

    /*========Content========*/
    /// <summary>Content-area bookkeeping -- see WindowGeometryState's doc comment for the same "grouped, plain fields" rationale. Named _contentState, not _content, to avoid colliding with the pluggable IWindowContent field below.</summary>
    private protected readonly WindowContentState _contentState = new();

    public Vector2 ContentAbsolutePosition => _contentState.AbsolutePosition;
    public Vector2 ContentSize => _contentState.Size;
    public Rectangle ContentRectangle => _contentState.Rectangle;
    public Vector2 ContentPadding { get; set; } = new(5, 5);
    public Color ContentColor => _contentState.BackgroundColor;

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

    public Window(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(windowService);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        FontService = fontService;
        TitleFont = fontService.GetFont(8);
        GlyphRenderer = glyphRenderer;
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
        _geometry.DisplayMode = layout?.DisplayMode ?? WindowDisplayMode.Fixed;
        _geometry.RelativePosition = layout?.RelativePosition ?? new Vector2();
        _geometry.AbsolutePosition = _parentWindow != null
            ? _parentWindow.ContentAbsolutePosition + _geometry.RelativePosition
            : _geometry.RelativePosition;

        _geometry.MinimumSize = layout?.MinimumSize ?? new Vector2(0, 0);
        _geometry.MaximumSize = layout?.MaximumSize ?? _parentWindow?.ContentSize ?? layout?.Size ?? new Vector2(0, 0);
        _geometry.OriginalSize = layout?.Size ?? new Vector2(0, 0);
        _geometry.CurrentSize = _geometry.OriginalSize;

        _isVisible = layout?.IsVisible ?? true;
        _isTransparent = layout?.IsTransparent ?? false;

        /*========Title========*/
        _title.ShowTitle = chrome?.ShowTitle ?? false;
        _title.ShowWhenMinimized = chrome?.ShowTitleWhenMinimized ?? false;
        _title.Text = chrome?.TitleText ?? string.Empty;
        _title.OriginalSize = new Vector2(_geometry.OriginalSize.X, TitleFont.MeasureString(" ").Y + TitlePadding.Y * 3);
        _title.Size = _title.OriginalSize;
        _title.BackgroundColor = chrome?.TitleColor ?? Color.LightBlue;
        _title.Buttons = [];

        /*========Border========*/
        _border.Show = chrome?.ShowBorder ?? false;
        _border.Thickness = BorderThickness.Uniform(chrome?.BorderSize ?? new Vector2(1, 1));

        /*========Content========*/
        _contentState.BackgroundColor = content?.ContentColor ?? Color.White;

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
        MeasureAndArrange();

        _windowViewport = new Viewport(_contentState.Rectangle);
        _cameraTransform = Matrix.CreateRotationZ(0) * // camera rotation, default 0
                           Matrix.CreateScale(new Vector3(1, 1, 1)); // TODO zoom

        if (_title.ShowTitle)
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

        foreach (var button in _title.Buttons)
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
        foreach (var button in _title.Buttons)
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

        if (_border.Show)
        {
            spriteBatch.Draw(unitRectangle, _border.TopRectangle, Color.Black);
            spriteBatch.Draw(unitRectangle, _border.BottomRectangle, Color.Black);
            spriteBatch.Draw(unitRectangle, _border.LeftRectangle, Color.Black);
            spriteBatch.Draw(unitRectangle, _border.RightRectangle, Color.Black);
        }

        if ((_geometry.DisplayMode != WindowDisplayMode.Minimized && _title.ShowTitle) || (_geometry.DisplayMode == WindowDisplayMode.Minimized && _title.ShowWhenMinimized))
        {
            if (!_isTransparent)
            {
                spriteBatch.Draw(unitRectangle, _title.Rectangle, _title.BackgroundColor);
            }
            spriteBatch.DrawString(TitleFont, TitleText, _title.AbsolutePosition + TitlePadding, Color.Black);

            foreach (var button in _title.Buttons)
            {
                button.Draw(gameTime, spriteBatch, unitRectangle);
            }
        }

        if (_geometry.DisplayMode != WindowDisplayMode.Minimized)
        {
            if (!_isTransparent)
            {
                spriteBatch.Draw(unitRectangle, _contentState.Rectangle, _contentState.BackgroundColor);
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

    public void HandleClick(Point mousePosition)
    {
        // _title.Rectangle is sized/positioned from _title.Size/_title.AbsolutePosition
        // unconditionally (RecalculateRectangles doesn't know about _title.ShowTitle), so
        // without this guard a titleless window's upper region is a dead zone: clicks there
        // land in an invisible title rect and route to HandleTitleClick (which only checks
        // title buttons, of which there are none) instead of falling through to content.
        if (_title.ShowTitle && _title.Rectangle.Contains(mousePosition))
        {
            HandleTitleClick(mousePosition);
        }
        else if (_contentState.Rectangle.Contains(mousePosition))
        {
            HandleContentClick(mousePosition);
        }
    }

    private void HandleTitleClick(Point mousePosition) => OnTitleClickAction(mousePosition);

    protected virtual void OnTitleClickAction(Point mousePosition)
    {
        foreach (var button in _title.Buttons)
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

        if (!_title.ShowTitle)
        {
            return;
        }

        var maximumIndex = _title.Buttons.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _title.Buttons.Insert(clampedInsertIndex, newButton);

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
        for (var index = 0; index < _title.Buttons.Count; index++)
        {
            var button = _title.Buttons[index];
            if (index == 0)
            {
                button.ChangeRelativePosition(new Vector2(_title.Size.X - button.Size.X - 3, 3));
            }
            else
            {
                var previousButton = _title.Buttons[index - 1];
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
                newChildWindow._geometry.RelativePosition = new Vector2(0, 0);
            }
            else
            {
                var previousChildWindow = _childWindows[updateIndex - 1];
                if (_childWindowTileMode == WindowTileMode.Horizontal)
                {
                    newChildWindow._geometry.RelativePosition = new Vector2(
                        previousChildWindow._geometry.RelativePosition.X + previousChildWindow._geometry.CurrentSize.X,
                        previousChildWindow._geometry.RelativePosition.Y);
                }
                else if (_childWindowTileMode == WindowTileMode.Vertical)
                {
                    newChildWindow._geometry.RelativePosition = new Vector2(
                        previousChildWindow._geometry.RelativePosition.X,
                        previousChildWindow._geometry.RelativePosition.Y + previousChildWindow._geometry.CurrentSize.Y);
                }
            }

            newChildWindow.Initialize();
        }

        // A WrapContent parent's own size depends on its children's -- re-fit around the
        // newly added child. Gated to WrapContent only: for Fixed/Fill/Minimized parents the
        // loop above already fully re-measures+re-arranges every affected child, and the
        // parent's own size never depends on children in those modes, so an unconditional
        // call here would just re-walk the entire existing sibling list for no effect.
        if (_geometry.DisplayMode == WindowDisplayMode.WrapContent)
        {
            MeasureAndArrange();
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

        // See the matching comment in AddChildWindow -- a WrapContent parent needs to shrink
        // to fit around the removed child; other modes don't depend on children for sizing.
        if (_geometry.DisplayMode == WindowDisplayMode.WrapContent)
        {
            MeasureAndArrange();
        }
    }

    /// <summary>
    /// Entry point for a full re-layout: measures this window's (and its subtree's) sizes
    /// bottom-up where needed, then arranges absolute positions/rectangles top-down. See
    /// Measure/Arrange below for why this is split into two passes rather than one.
    /// </summary>
    private void MeasureAndArrange()
    {
        // If there is no parent, this is a root window guaranteed to have a maximum set
        // (from BuildWindow) already sitting in _geometry.MaximumSize.
        var availableSize = _parentWindow != null
            ? _parentWindow.ContentSize - _geometry.RelativePosition
            : _geometry.MaximumSize;

        Measure(availableSize);
        Arrange();
    }

    /// <summary>
    /// Bottom-up size computation. WrapContent needs its children's sizes to compute its
    /// own, so it measures children first, threading its OWN available space through to them
    /// unchanged (the same resolution Android's View.measure() uses for WRAP_CONTENT: a
    /// wrap-content node's own final size isn't known yet, so it can't offer children a real
    /// constraint of its own -- it passes through what it was given). Fixed/Fill/Minimized's
    /// own size never depends on children, so they compute themselves first and hand children
    /// their own now-final content size as the real constraint.
    /// </summary>
    private void Measure(Vector2 availableSize)
    {
        var previousSize = _geometry.CurrentSize;
        _geometry.MaximumSize = availableSize;

        if (_geometry.DisplayMode == WindowDisplayMode.WrapContent)
        {
            MeasureChildren(availableSize);
            RecalculateWrapContentWindowSize();
        }
        else
        {
            switch (_geometry.DisplayMode)
            {
                case WindowDisplayMode.Minimized:
                    RecalculateMinimizedWindowSize();
                    break;
                case WindowDisplayMode.Fixed:
                    RecalculateFixedWindowSize();
                    break;
                case WindowDisplayMode.Fill:
                    RecalculateFillWindowSize();
                    break;
                default:
                    throw new NotImplementedException("No default window display mode.");
            }

            MeasureChildren(_contentState.Size);
        }

        if (_geometry.CurrentSize != previousSize)
        {
            Resized?.Invoke(this);
        }
    }

    private void MeasureChildren(Vector2 availableContentSize)
    {
        foreach (var childWindow in _childWindows)
        {
            childWindow.Measure(availableContentSize - childWindow.WindowRelativePosition);
        }
    }

    /// <summary>
    /// Top-down absolute-position/rectangle assignment, run only after every node in this
    /// subtree has already been measured (see Measure) -- RecalculateRectangles and
    /// RecalculateButtonsSizeAndPosition both read sizes that must already be final.
    /// </summary>
    protected void Arrange()
    {
        RecalculateAbsolutePositions();
        RecalculateRectangles();
        RecalculateButtonsSizeAndPosition();

        foreach (var childWindow in _childWindows)
        {
            childWindow.Arrange();
        }
    }

    private void RecalculateAbsolutePositions()
    {
        var previousAbsolutePosition = _geometry.AbsolutePosition;

        // A child's relative position is relative to the parent's content area, not the
        // parent's outer window bounds -- matching BuildWindow's own initial computation
        // below. Using WindowAbsolutePosition here instead used to shift every child by
        // exactly the parent's own border+title thickness, leaving e.g. a 1px gap between a
        // child's right border and the parent content's right edge whenever the parent had a
        // border.
        _geometry.AbsolutePosition = _parentWindow != null
            ? _parentWindow.ContentAbsolutePosition + _geometry.RelativePosition
            : _geometry.RelativePosition;

        _title.AbsolutePosition = _geometry.AbsolutePosition + BorderInset;

        _contentState.AbsolutePosition = new Vector2(
            _geometry.AbsolutePosition.X + BorderInset.X,
            _geometry.AbsolutePosition.Y + BorderInset.Y + TitleInsetHeight);

        if (_geometry.AbsolutePosition != previousAbsolutePosition)
        {
            Moved?.Invoke(this);
        }
    }

    protected virtual void RecalculateMinimizedWindowSize()
    {
        var textSize = TitleFont.MeasureString(_title.Text);

        // Widened to also fit the title buttons (close/minimize-restore), not just the text --
        // a short/empty title would otherwise shrink the title bar narrower than the buttons
        // it still has to hold, and RepositionTitleButtons (which doesn't know about text
        // width, only _title.Size.X) would tile them overlapping each other or the text.
        var titleWidth = System.Math.Max(textSize.X + TitlePadding.X * 2, TotalTitleButtonsWidth());
        _title.Size = new Vector2(titleWidth, textSize.Y + TitlePadding.Y * 2);

        _contentState.Size = new Vector2(0, 0);

        var windowSize = _title.Size + BorderInsetDoubled;

        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(windowSize.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X),
            windowSize.Y);
    }

    /// <summary>Minimum title width that fits every title button without overlap -- see RepositionTitleButtons for the matching 3px-gap tiling this mirrors.</summary>
    private float TotalTitleButtonsWidth()
    {
        if (_title.Buttons.Count == 0)
        {
            return 0f;
        }

        var width = 3f; // gap between the rightmost button and the title's right edge.
        foreach (var button in _title.Buttons)
        {
            width += button.Size.X + 3f; // each button plus the gap to its left.
        }

        return width;
    }

    protected virtual void RecalculateFixedWindowSize()
    {
        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(_geometry.OriginalSize.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X),
            MathHelper.Clamp(_geometry.OriginalSize.Y, _geometry.MinimumSize.Y, _geometry.MaximumSize.Y));

        // Resize horizontally to fit the new window size, but keep the vertical size.
        _title.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _title.OriginalSize.Y - BorderInset.Y);

        // Content sits below the title (which itself already accounts for just the top
        // border) and above the bottom border, so its own height must clear both the top and
        // bottom border -- BorderInsetDoubled.Y, not BorderInset.Y. Getting this wrong left no
        // room for a bottom border strip, so content's own background fill (drawn after the
        // border in Draw()) painted directly over it.
        _contentState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _geometry.CurrentSize.Y - BorderInsetDoubled.Y - TitleInsetHeight);
    }

    protected virtual void RecalculateFillWindowSize()
    {
        _geometry.CurrentSize = _geometry.MaximumSize;

        _title.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _title.OriginalSize.Y - BorderInset.Y);

        _contentState.Size = _geometry.CurrentSize
            - (_title.ShowTitle ? _title.Size : Vector2.Zero)
            - BorderInsetDoubled;
    }

    protected virtual void RecalculateWrapContentWindowSize()
    {
        if (_canContainChildWindows && _childWindows.Count > 0)
        {
            // Relative position + measured size, not the absolute WindowRectangle -- this
            // runs during Measure, before children have been Arranged, so their absolute
            // position isn't valid yet. Also more correct in general: fitting to children
            // shouldn't need absolute screen coordinates at all.
            var maxRight = 0f;
            var maxBottom = 0f;
            foreach (var childWindow in _childWindows)
            {
                maxRight = System.Math.Max(maxRight, childWindow.WindowRelativePosition.X + childWindow.WindowCurrentSize.X);
                maxBottom = System.Math.Max(maxBottom, childWindow.WindowRelativePosition.Y + childWindow.WindowCurrentSize.Y);
            }
            _contentState.Size = new Vector2(maxRight, maxBottom);
        }
        else
        {
            _contentState.Size = new Vector2(0, 0);
        }

        _contentState.Size += ContentPadding;

        _geometry.CurrentSize = _contentState.Size;
        if (_title.ShowTitle)
        {
            _title.Size = new Vector2(_contentState.Size.X, _title.OriginalSize.Y - BorderInset.Y);
            _geometry.CurrentSize += _title.Size;
        }
        _geometry.CurrentSize += BorderInsetDoubled;
    }

    /// <summary>Recalculates draw rectangles from the current absolute positions/sizes.</summary>
    private void RecalculateRectangles()
    {
        _geometry.Rectangle = new Rectangle((int)_geometry.AbsolutePosition.X, (int)_geometry.AbsolutePosition.Y, (int)_geometry.CurrentSize.X, (int)_geometry.CurrentSize.Y);
        _title.Rectangle = new Rectangle((int)_title.AbsolutePosition.X, (int)_title.AbsolutePosition.Y, (int)_title.Size.X, (int)_title.Size.Y);
        _contentState.Rectangle = new Rectangle((int)_contentState.AbsolutePosition.X, (int)_contentState.AbsolutePosition.Y, (int)_contentState.Size.X, (int)_contentState.Size.Y);

        RecalculateBorderRectangles();
    }

    /// <summary>
    /// Four edge strips, not one solid rectangle -- top/bottom span the window's full width
    /// (covering the corners) while left/right are inset by that thickness so all four tile
    /// the window's outline without overlapping.
    /// </summary>
    private void RecalculateBorderRectangles()
    {
        var topThickness = (int)_border.Thickness.Top;
        var bottomThickness = (int)_border.Thickness.Bottom;
        var leftThickness = (int)_border.Thickness.Left;
        var rightThickness = (int)_border.Thickness.Right;

        _border.TopRectangle = new Rectangle(_geometry.Rectangle.X, _geometry.Rectangle.Y, _geometry.Rectangle.Width, topThickness);
        _border.BottomRectangle = new Rectangle(_geometry.Rectangle.X, _geometry.Rectangle.Bottom - bottomThickness, _geometry.Rectangle.Width, bottomThickness);
        _border.LeftRectangle = new Rectangle(_geometry.Rectangle.X, _geometry.Rectangle.Y + topThickness, leftThickness, _geometry.Rectangle.Height - topThickness - bottomThickness);
        _border.RightRectangle = new Rectangle(_geometry.Rectangle.Right - rightThickness, _geometry.Rectangle.Y + topThickness, rightThickness, _geometry.Rectangle.Height - topThickness - bottomThickness);
    }

    /// <summary>
    /// Re-tiles every title button against the current title width (see
    /// RepositionTitleButtons) rather than just refreshing each button's absolute position
    /// from its previously-computed relative one, since the title width itself can change
    /// (e.g. minimizing shrinks it to fit just its text).
    /// </summary>
    private void RecalculateButtonsSizeAndPosition()
    {
        RepositionTitleButtons();
    }

    public void SetIsVisible(bool isVisible)
    {
        _isVisible = isVisible;
        _parentWindow?.MeasureAndArrange();
    }

    public void SetWindowDisplayMode(WindowDisplayMode newWindowDisplayMode)
    {
        // No-op guard matters beyond just skipping redundant work: without it, a call with
        // the mode the window is already in would still overwrite PreviousWindowDisplay with
        // that same current mode, corrupting what a later restore should return to.
        if (newWindowDisplayMode == _geometry.DisplayMode)
        {
            return;
        }

        _geometry.PreviousDisplayMode = _geometry.DisplayMode;
        _geometry.DisplayMode = newWindowDisplayMode;
        MeasureAndArrange();
        DisplayModeChanged?.Invoke(this);
    }

    /// <summary>
    /// Repositions the window relative to its parent (or the screen, for a root window).
    /// For chrome behaviors that need to move a window (e.g. a future drag-to-move) --
    /// Resized/Moved fire automatically through MeasureAndArrange.
    /// </summary>
    public void SetRelativePosition(Vector2 relativePosition)
    {
        _geometry.RelativePosition = relativePosition;
        MeasureAndArrange();
    }

    /// <summary>
    /// Sets the window's Fixed-mode size. Only display mode Fixed reads this size --
    /// Fill/WrapContent compute size from the parent/content instead, so this method has no
    /// visible effect on a Fill or WrapContent window, matching that a window can't be
    /// manually resized while it's set to auto-size.
    /// </summary>
    public void SetSize(Vector2 size)
    {
        _geometry.OriginalSize = size;
        MeasureAndArrange();
    }

    public void Close()
    {
        Closed?.Invoke(this);
        _windowService.CloseWindow(this);
    }
}
