using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;

namespace Presentation.UI;

// TODO minimize and restore: child windows should hide/show and the parent should
// resize to title-bar-only when minimized. Opened/Closed/Resized/Moved below are the
// event-based mechanism intended to drive that.
// TODO recalculate tiled sibling windows on minimize and restore
// TODO persist selectionWindow child windows until selection changes
// TODO click-and-drag create a semi-transparent "ghost" window that follows the cursor.
/// <summary>
/// The common base every interactive UI box derives from -- hierarchy, geometry/sizing,
/// border, glow, focus, click hit-testing, scrolling, input routing, and a generic header
/// region (reserved space above content, always drawn regardless of Minimized state when
/// ShowHeaderWhenMinimized). Window is Element plus a *text* header (title bar: text, buttons,
/// colors), pluggable IWindowContent, and close/minimize chrome. Folder is Element plus an
/// *icon* header -- see Folder.DrawHeader/OnHeaderClickAction -- with no need for any of
/// Window's title-text/content-hosting/chrome-behavior machinery. Button is Element plus a
/// single filled+bordered region with centered text, reusing Element's own geometry/border/
/// content state directly rather than a parallel copy (see Button.cs).
/// </summary>
public class Element
{
    public Guid ElementId { get; } = Guid.NewGuid();

    /// <summary>Internal, not protected: Button still needs its host window's FontService/WindowService to satisfy this same base constructor -- see Button's own constructor.</summary>
    internal FontService FontService { get; }

    private readonly ElementPoolService _elementPoolService;

    /// <summary>See FontService's doc comment -- same reason this is exposed internally.</summary>
    internal ElementPoolService ElementPoolService => _elementPoolService;

    /// <summary>Raised once the element has completed its initial setup.</summary>
    public event Action<Element>? Opened;

    /// <summary>Raised when the element is closed (before it's returned to ElementPoolService's pool).</summary>
    public event Action<Element>? Closed;

    /// <summary>
    /// Raised when the element's current size actually changes. Fires during the Measure
    /// pass, which runs across the whole subtree before Arrange (see Measure/Arrange) --
    /// so every node's Resized fires before any node's Moved, not interleaved node-by-node
    /// the way they were before the two-pass split.
    /// </summary>
    public event Action<Element>? Resized;

    /// <summary>Raised when the element's absolute screen position actually changes.</summary>
    public event Action<Element>? Moved;

    /// <summary>
    /// Raised whenever this element's content area is clicked, after OnContentClickAction
    /// (so subclass overrides like TextWindow's or MapWindow's own click handling still run
    /// first). Fired from HandleContentClick directly, not from the virtual
    /// OnContentClickAction, so subclasses that override it without calling base still raise
    /// this -- letting external code (e.g. NotificationCenter) react to a click without
    /// needing a dedicated Window subclass just to hook one in.
    /// </summary>
    public event Action<Element>? Clicked;

    /// <summary>
    /// Raised whenever this window's WindowDisplayMode actually changes, regardless of what
    /// triggered it -- not just its own chrome buttons. Lets external code (e.g. a future
    /// "minimize all" action, or a chrome behavior reacting to a mode it didn't itself set)
    /// react to the change without needing to be the one that called SetWindowDisplayMode.
    /// </summary>
    public event Action<Element>? DisplayModeChanged;

    /// <summary>Raised by a window that can't move focus itself (e.g. a TextBox submitting via Enter) to ask GameInputController to move it elsewhere -- see GameInputController.SetFocus, which subscribes/unsubscribes this the same way it does Closed.</summary>
    public event Action<Element>? FocusRequested;

    /// <summary>Events can only be raised from their declaring class, so subclasses (e.g. TextBox) go through this instead of invoking FocusRequested directly.</summary>
    protected void RequestFocus(Element targetElement) => FocusRequested?.Invoke(targetElement);

    /*========Hierarchy========*/
    protected Element? _parent;
    public Element? ParentElement => _parent;

    protected bool _canContainChildren;
    public bool CanContainChildren => _canContainChildren;

    protected ChildElementTileMode _childrenTileMode;
    public ChildElementTileMode ChildElementTileMode => _childrenTileMode;

    protected List<Element> _children = [];
    public List<Element> ChildElements => _children;

    /// <summary>
    /// Position/size/display-mode bookkeeping grouped into one object -- see
    /// ElementGeometryState -- rather than several independent fields, so a future
    /// WindowMoveBehavior/WindowResizeBehavior (attached the same way CloseBehavior is)
    /// has a single cohesive surface to read/mutate instead of reaching into many.
    /// </summary>
    private protected readonly ElementGeometryState _geometry = new();

    public ElementDisplayMode DisplayMode => _geometry.DisplayMode;
    public ElementDisplayMode PreviousDisplay => _geometry.PreviousDisplayMode;
    public Vector2 AbsolutePosition => _geometry.AbsolutePosition;
    public Vector2 RelativePosition => _geometry.RelativePosition;
    public Vector2 OriginalSize => _geometry.OriginalSize;
    public Vector2 CurrentSize => _geometry.CurrentSize;
    public Vector2 MinimumSize => _geometry.MinimumSize;
    public Vector2 MaximumSize => _geometry.MaximumSize;
    public Rectangle Rectangle => _geometry.Rectangle;

    protected bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    protected bool _isTransparent;
    public bool IsTransparent => _isTransparent;

    protected bool _isGlowing;
    public bool IsGlowing => _isGlowing;

    protected Color _glowColor = Color.Gold;
    public Color GlowColor => _glowColor;

    /// <summary>Turns the outward glow (see GlowRenderer) on/off around this window's border -- e.g. NotificationCenter's Folder while any category has an unread notification.</summary>
    public void SetGlow(bool isGlowing, Color? color = null)
    {
        _isGlowing = isGlowing;
        _glowColor = color ?? Color.Gold;
    }

    /*========Focus========*/
    public event Action<Element>? FocusChanged;

    protected bool _isFocused;

    /// <summary>True while this window holds input focus -- set by GameInputController, not this window itself.</summary>
    public bool IsFocused => _isFocused;

    /*========Header========*/
    /// <summary>Internal, not protected, for the same reason as WindowService/FontService -- Button uses this to center its label the same way GlyphRenderer centers map tile glyphs.</summary>
    internal GlyphRenderer GlyphRenderer { get; }

    /// <summary>
    /// Generic header-region bookkeeping -- see ElementHeaderState's own doc comment. Window's
    /// title text/buttons/colors and Folder's icon are drawn by their own DrawHeader override;
    /// this only holds what the shared Measure/Arrange/Draw/click-routing pipeline needs.
    /// </summary>
    private protected readonly ElementHeaderState _headerState = new();

    public bool ShowHeader => _headerState.ShowHeader;
    public bool ShowHeaderWhenMinimized => _headerState.ShowHeaderWhenMinimized;
    public Vector2 OriginalHeaderSize => _headerState.OriginalSize;
    public Vector2 HeaderSize => _headerState.Size;
    public Vector2 HeaderAbsolutePosition => _headerState.AbsolutePosition;
    public Rectangle HeaderRectangle => _headerState.Rectangle;

    /// <summary>Header height if shown, else zero -- see BorderInset for the analogous border helper.</summary>
    protected float HeaderInsetHeight =>
        _headerState.ShowHeader
            ? _headerState.Size.Y
            : 0;

    /// <summary>Natural width the header needs for its own content, independent of the element's own content area -- e.g. Window measures its title text plus buttons; a header-less Element (or Folder, whose icon never needs more width than its content already provides) keeps the default of zero.</summary>
    protected virtual float MinimumHeaderWidth() => 0f;

    /// <summary>Draws this element's header region -- Window's title bar (background/text/buttons) and Folder's icon are both just their own override of this, called from Draw whenever the header is shown (see the ShowHeader/ShowHeaderWhenMinimized gate there). No-op by default.</summary>
    protected virtual void DrawHeader(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) { }

    /// <summary>Extra per-frame Initialize work the header needs -- Window initializes its title buttons here. No-op by default.</summary>
    protected virtual void InitializeHeaderExtras() { }

    /// <summary>Extra per-frame Update work the header needs -- Window updates its title buttons here. No-op by default.</summary>
    protected virtual void UpdateHeaderExtras(GameTime gameTime) { }

    /// <summary>Extra per-Arrange work the header needs -- Window re-tiles its title buttons here (see RepositionTitleButtons), since the header's own width can change (e.g. minimizing shrinks it to fit just its text). No-op by default.</summary>
    protected virtual void RecalculateHeaderExtras() { }

    /*========Border========*/
    /// <summary>Border bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale.</summary>
    private protected readonly ElementBorderState _border = new();

    public bool ShowBorder => _border.Show;

    /// <summary>
    /// Border thickness on one edge if the border is shown, else zero -- single source of
    /// truth for how much border eats into title/content space, replacing what used to be an
    /// independently-repeated `_showBorder ? _borderSize.X : 0`-style ternary in every
    /// RecalculateXxxWindowSize method and RecalculateAbsolutePositions.
    /// </summary>
    protected Vector2 BorderInset =>
        _border.Show
            ? new Vector2(_border.Thickness.Left, _border.Thickness.Top)
            : Vector2.Zero;

    /// <summary>Border thickness on both edges of an axis (e.g. left+right) if shown, else zero.</summary>
    protected Vector2 BorderInsetDoubled =>
        _border.Show
            ? new Vector2(_border.Thickness.Horizontal, _border.Thickness.Vertical)
            : Vector2.Zero;

    public Rectangle BorderTopRectangle => _border.TopRectangle;
    public Rectangle BorderBottomRectangle => _border.BottomRectangle;
    public Rectangle BorderLeftRectangle => _border.LeftRectangle;
    public Rectangle BorderRightRectangle => _border.RightRectangle;
    public BorderStyle BorderStyle => _border.Style;

    /*========Content========*/
    /// <summary>Content-area bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale. Named _contentState, not _content, to avoid colliding with the name of Window's own pluggable IElementContent field.</summary>
    private protected readonly ElementContentState _contentState = new();

    public Vector2 ContentAbsolutePosition => _contentState.AbsolutePosition;
    public Vector2 ContentSize => _contentState.Size;
    public Rectangle ContentRectangle => _contentState.Rectangle;
    public Vector2 ContentPadding { get; set; } = new(5, 5);
    public Color ContentColor => _contentState.BackgroundColor;

    /*========Viewport========*/
    private Viewport _viewport;
    public Viewport Viewport => _viewport;

    private Matrix _cameraTransform;
    public Matrix CameraTransform => _cameraTransform;

    /*========User Controls========*/
    public bool CanUserMove { get; set; }
    public bool CanUserResize { get; set; }
    public bool CanUserScrollHorizontal { get; set; }
    public bool CanUserScrollVertical { get; set; }

    /// <summary>Unlike the other CanUserXxx flags (opt-in, default false), this defaults to true -- every window participates in focus unless explicitly opted out.</summary>
    public bool CanUserFocus { get; set; }

    protected virtual bool RequiresContentViewport => CanUserScrollHorizontal || CanUserScrollVertical;

    /*========Scroll========*/
    private Vector2 _scrollOffset;
    public Vector2 ScrollOffset => _scrollOffset;
    protected Vector2 _maxScrollOffset;
    public Vector2 MaxScrollOffset => _maxScrollOffset;

    public Element(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(elementPoolService);
        ArgumentNullException.ThrowIfNull(glyphRenderer);

        FontService = fontService;
        GlyphRenderer = glyphRenderer;
        _elementPoolService = elementPoolService;
    }

    public virtual void Build(Element? parent, ElementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hierarchy = options.Hierarchy;
        var layout = options.Layout;
        var chrome = options.Chrome;
        var content = options.Content;

        /*========Hierarchy========*/
        _parent = parent;
        _canContainChildren = hierarchy?.CanContainChildren ?? false;
        _childrenTileMode = hierarchy?.ChildrenTileMode ?? ChildElementTileMode.Floating;
        _children = [];

        /*========Display========*/
        _geometry.DisplayMode = layout?.DisplayMode ?? ElementDisplayMode.Fixed;
        _geometry.RelativePosition = layout?.RelativePosition ?? new Vector2();
        _geometry.AbsolutePosition = _parent != null
            ? _parent.ContentAbsolutePosition + _geometry.RelativePosition
            : _geometry.RelativePosition;

        _geometry.MinimumSize = layout?.MinimumSize ?? new Vector2(0, 0);
        _geometry.MaximumSize = layout?.MaximumSize ?? _parent?.ContentSize ?? layout?.Size ?? new Vector2(0, 0);
        _geometry.OriginalSize = layout?.Size ?? new Vector2(0, 0);
        _geometry.CurrentSize = _geometry.OriginalSize;

        _isVisible = layout?.IsVisible ?? true;
        _isTransparent = layout?.IsTransparent ?? false;

        // Pooled windows must not inherit a stale glow from whatever they were last used for --
        // same rationale as _isFocused reset below.
        _isGlowing = false;
        _glowColor = Color.Gold;

        /*========Focus========*/
        // Pooled windows (see WindowService) must not inherit a stale focused look from
        // whatever they were last used for.
        _isFocused = false;

        /*========Header========*/
        // Only the generic visibility flags -- a header's actual size (text-measured for
        // Window, icon-sized for Folder) is set by the concrete subclass's own BuildWindow
        // override afterward, the same "call base then override" pattern Folder already used
        // even before this generalization.
        _headerState.ShowHeader = chrome?.ShowTitle ?? false;
        _headerState.ShowHeaderWhenMinimized = chrome?.ShowTitleWhenMinimized ?? false;

        /*========Border========*/
        _border.Show = chrome?.ShowBorder ?? false;
        _border.Thickness = BorderThickness.Uniform(chrome?.BorderSize ?? new Vector2(1, 1));
        _border.Style = chrome?.BorderStyle ?? BorderStyle.Flat;

        /*========Content========*/
        _contentState.BackgroundColor = content?.ContentColor ?? Color.White;

        /*========User Controls========*/
        CanUserMove = chrome?.CanUserMove ?? false;
        CanUserResize = chrome?.CanUserResize ?? false;
        CanUserScrollHorizontal = chrome?.CanUserScrollHorizontal ?? false;
        CanUserScrollVertical = chrome?.CanUserScrollVertical ?? false;
        CanUserFocus = chrome?.CanUserFocus ?? true;

        /*========Scroll========*/
        _scrollOffset = Vector2.Zero;
        _maxScrollOffset = Vector2.Zero;
    }

    public virtual void Initialize()
    {
        MeasureAndArrange();

        RecalculateCameraTransform(); // TODO zoom/rotation, if ever needed -- see RecalculateCameraTransform.

        InitializeHeaderExtras();

        foreach (var childElement in _children)
        {
            childElement.Initialize();
        }

        // Runs after the loop above (not before): content that adds its own child windows
        // (e.g. SelectionWindowContent) does so via AddChild, which already initializes each
        // window it adds -- running this first would let its children get caught by the loop
        // above and initialized a second time. And before Opened (not after): Opened is meant
        // to signal the element is fully set up, content included.
        OnChildrenInitialized();

        Opened?.Invoke(this);
    }

    /// <summary>No-op by default; Window overrides this to initialize its pluggable IElementContent.</summary>
    protected virtual void OnChildrenInitialized() { }

    /// <summary>
    /// Scrolls this window's content by delta, clamped to [0, MaxScrollOffset] -- an axis
    /// CanUserScrollHorizontal/Vertical doesn't allow is held at zero regardless of delta, so
    /// a window that only opts into vertical scrolling (the common case -- see TextWindow,
    /// where word-wrap already keeps lines within the content width) simply ignores any
    /// horizontal delta a caller passes. Folded into CameraTransform (see
    /// RecalculateCameraTransform) rather than requiring every DrawContent implementation to
    /// manually offset its own draw calls -- content keeps drawing in the same local
    /// coordinates it always has; RequiresContentViewport's per-window viewport (see Draw)
    /// picks the offset up automatically.
    /// </summary>
    public void ScrollBy(Vector2 delta)
    {
        var desiredOffset = _scrollOffset + delta;
        _scrollOffset = new Vector2(
            CanUserScrollHorizontal
                ? MathHelper.Clamp(desiredOffset.X, 0, _maxScrollOffset.X)
                : 0,
            CanUserScrollVertical
                ? MathHelper.Clamp(desiredOffset.Y, 0, _maxScrollOffset.Y)
                : 0);

        RecalculateCameraTransform();

        // Child windows (as opposed to DrawContent, e.g. TextWindow's own wrapped text) aren't
        // drawn through the CameraTransform pass at all -- see Draw's child-window loop, which
        // runs outside RequiresContentViewport's transformed/clipped Begin/End pair -- so
        // ScrollOffset only moves them at all because RecalculateAbsolutePositions folds it in
        // directly. Re-arranging here is what actually pushes a scroll into their positions.
        Arrange();
    }

    /// <summary>
    /// Sets how far this window's content can scroll (see ScrollBy) and re-clamps the current
    /// ScrollOffset into the new bounds -- e.g. TextWindow calls this whenever its wrapped text
    /// or content size changes, since a resize or text update can shrink MaxScrollOffset below
    /// wherever the window was previously scrolled to.
    /// </summary>
    protected void SetMaxScrollOffset(Vector2 maxScrollOffset)
    {
        _maxScrollOffset = Vector2.Max(Vector2.Zero, maxScrollOffset);
        ScrollBy(Vector2.Zero);
    }

    /// <summary>
    /// -ScrollOffset as a translation: content drawn in local coordinates (i.e. under
    /// RequiresContentViewport's own Begin(..., CameraTransform) pass, see Draw) shifts by
    /// this before the per-window viewport maps it onto screen, which is what makes scrolling
    /// work without any DrawContent implementation needing to know about ScrollOffset itself.
    /// No rotation/zoom yet -- CameraTransform predates ScrollOffset and was already a
    /// placeholder for both; scrolling is the first thing to actually need it.
    /// </summary>
    private void RecalculateCameraTransform()
    {
        _cameraTransform = Matrix.CreateTranslation(-_scrollOffset.X, -_scrollOffset.Y, 0);
    }

    public virtual void LoadContent()
    {
        foreach (var childElement in _children)
        {
            childElement.LoadContent();
        }
    }

    public virtual void Update(GameTime gameTime)
    {
        UpdateHeaderExtras(gameTime);

        foreach (var childElement in _children)
        {
            childElement.Update(gameTime);
        }
    }

    public virtual void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_isVisible)
        {
            return;
        }

        if (_border.Show)
        {
            BorderRenderer.Draw(spriteBatch, unitRectangle, _border.Style, _border.TopRectangle, _border.BottomRectangle, _border.LeftRectangle, _border.RightRectangle);
        }

        if (_isGlowing)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, _geometry.Rectangle, _glowColor);
        }

        if ((_geometry.DisplayMode != ElementDisplayMode.Minimized && _headerState.ShowHeader) || (_geometry.DisplayMode == ElementDisplayMode.Minimized && _headerState.ShowHeaderWhenMinimized))
        {
            DrawHeader(gameTime, spriteBatch, unitRectangle);
        }

        if (_geometry.DisplayMode != ElementDisplayMode.Minimized)
        {
            if (!_isTransparent)
            {
                spriteBatch.Draw(unitRectangle, _contentState.Rectangle, _contentState.BackgroundColor);
            }

            if (RequiresContentViewport)
            {
                // A dedicated viewport translates this window's local-coordinate content onto
                // screen and hard-clips whatever overflows it
                spriteBatch.End();

                var previousViewport = graphicsDevice.Viewport;
                graphicsDevice.Viewport = Viewport;
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, CameraTransform);

                DrawContent(gameTime, spriteBatch, unitRectangle);

                spriteBatch.End();
                graphicsDevice.Viewport = previousViewport;
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            }
            else
            {
                DrawContent(gameTime, spriteBatch, unitRectangle);
            }

            foreach (var childElement in _children)
            {
                childElement.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
            }
        }
    }

    /// <summary>No-op by default; TextWindow/MapWindow override this directly, Window overrides it to host IElementContent.</summary>
    public virtual void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) { }

    /// <summary>Routes a key newly pressed this frame to this window while it holds focus -- see GameInputController.RouteKeyPressesToFocusedWindow.</summary>
    internal void HandleKeyPress(Keys key) => OnKeyPressAction(key);

    protected virtual void OnKeyPressAction(Keys key) { }

    /// <summary>
    /// Routes the whole keyboard state to this window once per frame while it holds focus --
    /// see GameInputController.RouteHotkeysToFocusedWindow. Unlike HandleKeyPress (one discrete
    /// key-press event at a time), this is for windows whose own hotkeys need continuous or
    /// combined multi-key state (e.g. MapWindow's WASD scroll, which reads all four keys'
    /// current down-state together rather than reacting to one press event).
    /// </summary>
    internal void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => OnHotkeysAction(keyboardState, previousKeyboardState);

    protected virtual void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState) { }

    /// <summary>Shared "newly pressed this frame" edge-detection for OnHotkeysAction overrides and GameInputController's own Tab handling.</summary>
    internal static bool WasKeyPressed(KeyboardState current, KeyboardState previous, Keys key) => current.IsKeyDown(key) && previous.IsKeyUp(key);

    /// <summary>
    /// Fires once when a right-mouse-button drag starts over this window -- see
    /// GameInputController's right-button state machine. No-op by default; MapWindow uses
    /// this to snapshot its own scroll position as the drag's anchor, so every subsequent
    /// HandleRightDrag call (which reports the *total* delta since the drag started, not a
    /// per-frame increment) can recompute the desired scroll from a fixed reference point
    /// rather than accumulating potentially-lossy per-frame deltas.
    /// </summary>
    internal void HandleRightDragStart() => OnRightDragStartAction();

    protected virtual void OnRightDragStartAction() { }

    /// <summary>
    /// Routes the total mouse-pixel delta of an in-progress right-mouse-button drag (measured
    /// from where the drag started, not since the last frame) to whichever window the drag
    /// started over -- see GameInputController's right-button state machine and
    /// HandleRightDragStart. No-op by default (only MapWindow overrides this today, to pan its
    /// camera); unlike HandleHotkeys this doesn't depend on focus, since a drag-to-pan gesture
    /// shouldn't require clicking to focus a window first.
    /// </summary>
    internal void HandleRightDrag(Vector2 totalPixelDeltaSinceStart) => OnRightDragAction(totalPixelDeltaSinceStart);

    protected virtual void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) { }

    /// <summary>
    /// Fires once when an in-progress right-mouse-button drag ends (button released) -- see
    /// GameInputController's right-button state machine. No-op by default; MapWindow uses this
    /// to settle its smooth sub-tile scroll offset onto the tile grid once the gesture is over,
    /// rather than mid-drag on every frame.
    /// </summary>
    internal void HandleRightDragEnd() => OnRightDragEndAction();

    protected virtual void OnRightDragEndAction() { }

    /// <summary>
    /// Fires instead of HandleRightDragEnd when a right-button press/release never moved the
    /// mouse past GameInputController's small tap-vs-drag pixel threshold -- a right-click
    /// "tap," distinct from the drag-to-pan gesture the same button also drives. No-op by
    /// default; MapWindow uses this to cancel an armed ability, since a genuine right-drag
    /// (panning the camera) must keep behaving exactly as it already does.
    /// </summary>
    internal void HandleRightClickTap() => OnRightClickTapAction();

    protected virtual void OnRightClickTapAction() { }

    /// <summary>
    /// Fires on every root/HUD window when Escape is pressed -- see
    /// GameInputController.HandleEscape for why this is broadcast unconditionally rather than
    /// routed only to whichever window holds focus. No-op by default; MapWindow uses this to
    /// cancel an armed ability or an in-progress Delayed action windup, the same cancellation
    /// OnRightClickTapAction triggers.
    /// </summary>
    internal void HandleEscape() => OnEscapeAction();

    protected virtual void OnEscapeAction() { }

    /// <summary>
    /// Routes an actual typed character (shifted case, punctuation, OS keyboard layout) to
    /// this window while it holds focus -- see GameInputController.RouteTextInputToFocusedWindow.
    /// Neither HandleKeyPress (raw Keys values) nor HandleHotkeys (modifier-aware combos) can
    /// deliver real characters; this is fed from FNA's TextInputEXT.
    /// </summary>
    internal void HandleTextInput(char character) => OnTextInputAction(character);

    protected virtual void OnTextInputAction(char character) { }

    /// <summary>
    /// Finds the next focusable TextBox among this window's direct children, starting right
    /// after `after` and wrapping around (after: null means "the first one"). Shared by
    /// TextBox's own Enter-to-advance and GameInputController.SetFocus's auto-redirect into a
    /// container's first TextBox -- both are "find the next TextBox sibling," the second just
    /// with after: null.
    /// </summary>
    internal Element? NextFocusableDescendant(Element? after)
    {
        // after: null starts the scan at index 0 (IndexOf(null) is -1, so +1 lands on 0) and
        // never matches the candidate == after break check below, since _childElements never
        // contains a null entry -- the scan just runs to completion in order, i.e. "the first
        // one." after: some child starts right after it and wraps around, stopping once the
        // scan loops all the way back to after itself (a lone TextBox must not "advance" to
        // itself) rather than re-checking it as index Count.
        var startOffset = after is null
            ? 0
            : _children.IndexOf(after) + 1;

        for (var offset = 0; offset < _children.Count; offset++)
        {
            var candidate = _children[(startOffset + offset) % _children.Count];
            if (candidate == after)
            {
                break;
            }
            if (candidate is TextBox { CanUserFocus: true })
            {
                return candidate;
            }
        }

        return null;
    }

    public void HandleClick(Point mousePosition)
    {
        // _header.Rectangle is sized/positioned from _header.Size/_header.AbsolutePosition
        // unconditionally (RecalculateRectangles doesn't know about _header.ShowHeader), so
        // without this guard a header-less element's upper region is a dead zone: clicks there
        // land in an invisible header rect and route to HandleHeaderClick (which only checks
        // header buttons, of which there are none) instead of falling through to content.
        if (_headerState.ShowHeader && _headerState.Rectangle.Contains(mousePosition))
        {
            HandleHeaderClick(mousePosition);
        }
        else if (_contentState.Rectangle.Contains(mousePosition))
        {
            HandleContentClick(mousePosition);
        }
    }

    private void HandleHeaderClick(Point mousePosition) => OnHeaderClickAction(mousePosition);

    /// <summary>No-op by default -- Window overrides this to dispatch to title buttons; Folder overrides it to toggle expand/collapse.</summary>
    protected virtual void OnHeaderClickAction(Point mousePosition) { }

    /// <summary>
    /// Hit-test only -- returns the header button at this point without invoking its click
    /// action, unlike OnHeaderClickAction. Null by default (most Elements have no header
    /// buttons at all); Window overrides this with its real title-button lookup.
    /// </summary>
    protected virtual Button? FindHeaderButtonAt(Point position) => null;

    /// <summary>How close (in pixels) to a corner of WindowRectangle counts as grabbing that corner for a two-axis resize, rather than just one edge.</summary>
    /// <summary>
    /// How close (in pixels) to WindowRectangle's edge counts as grabbing it for resize --
    /// deliberately independent of the visual border thickness (BorderThickness.Uniform
    /// defaults to just 1px), which would make single-edge grabbing nearly impossible to hit
    /// precisely with a mouse. Matches roughly what desktop OSes use for their own resize
    /// border (e.g. Windows' classic ~8px non-DPI-scaled resize frame), bumped slightly for
    /// comfortable grabbing.
    /// </summary>
    private const int ResizeGrabSize = 10;

    /// <summary>
    /// Which border edge(s) this point is a resize-grab for, if any -- an input-only zone
    /// ResizeGrabSize wide along each edge of WindowRectangle (corners, where two edges
    /// overlap, checked first), entirely independent of the border's own visual rectangles/
    /// thickness. Pure geometry -- the caller (TryHitTestInteraction) decides whether
    /// CanUserResize even allows starting one.
    /// </summary>
    internal ResizeEdges GetResizeEdgesAt(Point position)
    {
        if (!_border.Show)
        {
            return ResizeEdges.None;
        }

        // ResizeEdges is [Flags] -- a corner is just its two edges OR'd together, so there's
        // no need to enumerate the four corner-then-edge cases by hand; whichever edges the
        // position is close to combine on their own.
        var rect = _geometry.Rectangle;
        var edges = ResizeEdges.None;

        if (position.Y - rect.Y < ResizeGrabSize)
        {
            edges |= ResizeEdges.Top;
        }
        if (rect.Bottom - position.Y < ResizeGrabSize)
        {
            edges |= ResizeEdges.Bottom;
        }
        if (position.X - rect.X < ResizeGrabSize)
        {
            edges |= ResizeEdges.Left;
        }
        if (rect.Right - position.X < ResizeGrabSize)
        {
            edges |= ResizeEdges.Right;
        }

        return edges;
    }

    /// <summary>
    /// Picks exactly one interaction target, topmost-first -- unlike OnContentClickAction
    /// (which loops every overlapping child with no break, fine for plain clicks but wrong
    /// for picking a single drag target). Checks, in priority order: header buttons (a button
    /// always wins over starting a move, even if CanUserMove) -> header minus buttons
    /// (move, if CanUserMove) -> border edges/corners (resize, if CanUserResize) -> children,
    /// topmost (last-added, see AddChildWindow) first -> this window's own content as the
    /// fallback. Returns ElementInteraction.NotHit if this point isn't even within
    /// WindowRectangle, so callers can walk sibling root windows/tiers.
    /// </summary>
    /// <summary>
    /// True unless a tiling parent (Horizontal/Vertical) owns this window's relative position --
    /// dragging it would just be overwritten by the parent's next AddChildWindow/
    /// RemoveChildWindow re-tile. Root windows (no parent) and Floating children (whose own doc
    /// comment already says "the creator sets relative position", i.e. free positioning is the
    /// point) are unaffected. Gates drag-to-move (Window Chrome Phase C) and will gate
    /// drag-to-resize (Phase D) the same way.
    /// </summary>
    private bool HasFreePosition => _parent is null || _parent.ChildElementTileMode == ChildElementTileMode.Floating;

    internal ElementInteraction TryHitTestInteraction(Point position)
    {
        if (!_geometry.Rectangle.Contains(position))
        {
            return ElementInteraction.NotHit;
        }

        var button = FindHeaderButtonAt(position);
        if (button is not null)
        {
            return ElementInteraction.ButtonClick(this, button);
        }

        if (CanUserMove && HasFreePosition && _headerState.ShowHeader && _headerState.Rectangle.Contains(position))
        {
            return ElementInteraction.Move(this);
        }

        // Fixed-only: SetSize/SetBounds's own resize math is documented as only affecting
        // Fixed windows (Fill/WrapContent compute their size from the parent/content instead),
        // so a Fill/WrapContent window offering a Resize interaction here would let the user
        // start a drag that never visibly does anything. Same no-tiling-parent restriction as
        // Move (HasFreePosition) -- a tiled child's size is also just recomputed on the next
        // AddChildWindow/RemoveChildWindow, so manual resize would just be fought the same way.
        if (CanUserResize && HasFreePosition && _geometry.DisplayMode == ElementDisplayMode.Fixed)
        {
            var edges = GetResizeEdgesAt(position);
            if (edges != ResizeEdges.None)
            {
                return ElementInteraction.Resize(this, edges);
            }
        }

        for (var index = _children.Count - 1; index >= 0; index--)
        {
            var childInteraction = _children[index].TryHitTestInteraction(position);
            if (childInteraction.Element is not null)
            {
                return childInteraction;
            }
        }

        return ElementInteraction.Click(this);
    }

    /// <summary>
    /// Moves this window to the end of its parent's child list, so it draws last (on top) and
    /// wins future overlapping hit-tests against its siblings. No-op for a root window (no
    /// parent) -- GameInputController is responsible for raising a root window to the front of
    /// whichever shared tier list (BaseWindows/StaticHudWindows/DynamicHudWindows/UserWindows)
    /// it belongs to, since Window itself has no knowledge of those.
    /// </summary>
    internal void RaiseToFront()
    {
        if (_parent is null)
        {
            return;
        }

        var siblings = _parent._children;
        siblings.Remove(this);
        siblings.Add(this);
    }

    private void HandleContentClick(Point mousePosition)
    {
        OnContentClickAction(mousePosition);
        Clicked?.Invoke(this);
    }

    protected virtual void OnContentClickAction(Point mousePosition)
    {
        foreach (var childElement in _children)
        {
            if (childElement.Rectangle.Contains(mousePosition))
            {
                childElement.HandleClick(mousePosition);
            }
        }
    }

    public void AddChild(Element newChild, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(newChild);

        if (!_canContainChildren)
        {
            return;
        }

        var maximumIndex = _children.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _children.Insert(clampedInsertIndex, newChild);

        // Retiles from the insertion point onward, not just newChildWindow itself -- inserting
        // anywhere but the end shifts every sibling after it one slot down the tiling axis too.
        RetileChildrenFrom(clampedInsertIndex);

        // A WrapContent parent's own size depends on its children's -- re-fit around the
        // newly added child. Gated to WrapContent only: for Fixed/Fill/Minimized parents the
        // loop above already fully re-measures+re-arranges every affected child, and the
        // parent's own size never depends on children in those modes, so an unconditional
        // call here would just re-walk the entire existing sibling list for no effect.
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            MeasureAndArrange();
        }

        // A scrollable parent's own MaxScrollOffset depends on its children's total extent the
        // same way a WrapContent parent's own size does above -- re-fit it here, and keep it
        // in sync with newChildWindow's own future resizes (e.g. SelectionWindowContent
        // refreshing a component TextWindow's text) via Resized, the same way TextBox tells a
        // WrapContent parent to re-fit after resizing itself (see TextBox.AutoSizeToContent).
        if (CanUserScrollVertical || CanUserScrollHorizontal)
        {
            newChild.Resized += OnChildElementResizedForScrollBounds;
            RecalculateScrollBoundsFromChildren();
        }
    }

    /// <summary>Removes the child, then retiles everything after it so later siblings close the gap instead of keeping the removed window's slot as dead space.</summary>
    public void RemoveChild(Guid elementId)
    {
        var childElementIndex = _children.FindIndex(childElement => childElement.ElementId == elementId);
        if (childElementIndex < 0)
        {
            return;
        }

        var removedChild = _children[childElementIndex];
        _children.RemoveAt(childElementIndex);
        RetileChildrenFrom(childElementIndex);

        // See the matching comment in AddChildWindow -- a WrapContent parent needs to shrink
        // to fit around the removed child; other modes don't depend on children for sizing.
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            MeasureAndArrange();
        }

        if (CanUserScrollVertical || CanUserScrollHorizontal)
        {
            removedChild.Resized -= OnChildElementResizedForScrollBounds;
            RecalculateScrollBoundsFromChildren();
        }
    }

    private void OnChildElementResizedForScrollBounds(Element _) => RecalculateScrollBoundsFromChildren();

    /// <summary>
    /// A scrollable parent's MaxScrollOffset is how far its children's total extent exceeds
    /// its own (fixed) content size -- the same maxRight/maxBottom-from-children computation
    /// RecalculateWrapContentWindowSize uses to size a WrapContent parent around its children,
    /// applied here to bound scrolling instead of sizing.
    /// </summary>
    private void RecalculateScrollBoundsFromChildren()
    {
        var maxRight = 0f;
        var maxBottom = 0f;

        foreach (var childElement in _children)
        {
            maxRight = System.Math.Max(maxRight, childElement.RelativePosition.X + childElement.CurrentSize.X);
            maxBottom = System.Math.Max(maxBottom, childElement.RelativePosition.Y + childElement.CurrentSize.Y);
        }

        SetMaxScrollOffset(new Vector2(maxRight - _contentState.Size.X, maxBottom - _contentState.Size.Y));
    }

    /// <summary>
    /// Recomputes RelativePosition for every child from startIndex onward against
    /// _childWindowTileMode -- Horizontal/Vertical chain each child off the previous sibling's
    /// now-current position+size (in that order: Initialize below re-measures a child's
    /// CurrentSize, which the *next* iteration's chaining depends on), Floating leaves position
    /// alone entirely (the creator owns it). Shared by AddChildWindow (inserting anywhere but
    /// the end shifts every later sibling down the tiling axis) and RemoveChildWindow (closing
    /// the gap the removed window leaves behind) rather than each hand-rolling the same chain.
    /// </summary>
    private void RetileChildrenFrom(int startIndex)
    {
        for (var index = startIndex; index < _children.Count; index++)
        {
            var childElement = _children[index];

            if (_childrenTileMode == ChildElementTileMode.Floating)
            {
                // Let the window's creator determine its relative position and draw order.
            }
            else if (index == 0)
            {
                childElement._geometry.RelativePosition = new Vector2(0, 0);
            }
            else
            {
                var previousChildElement = _children[index - 1];
                if (_childrenTileMode == ChildElementTileMode.Horizontal)
                {
                    childElement._geometry.RelativePosition = new Vector2(
                        previousChildElement._geometry.RelativePosition.X + previousChildElement._geometry.CurrentSize.X,
                        previousChildElement._geometry.RelativePosition.Y);
                }
                else if (_childrenTileMode == ChildElementTileMode.Vertical)
                {
                    childElement._geometry.RelativePosition = new Vector2(
                        previousChildElement._geometry.RelativePosition.X,
                        previousChildElement._geometry.RelativePosition.Y + previousChildElement._geometry.CurrentSize.Y);
                }
            }

            childElement.Initialize();
        }
    }

    /// <summary>
    /// Entry point for a full re-layout: measures this window's (and its subtree's) sizes
    /// bottom-up where needed, then arranges absolute positions/rectangles top-down. See
    /// Measure/Arrange below for why this is split into two passes rather than one.
    /// </summary>
    /// <remarks>
    /// Internal, not private: a WrapContent parent's own size depends on its children's (see
    /// RecalculateWrapContentWindowSize), but nothing propagates that automatically when a
    /// child resizes *itself* after already being attached -- AddChildWindow/RemoveChildWindow
    /// re-fit the parent on attach/detach, but a child calling its own SetSize/SetBounds later
    /// (e.g. TextBox.AutoSizeToContent, growing as the user types) has no such hook. Exposing
    /// this lets a child ask ParentElement to re-measure itself directly rather than needing a
    /// whole new "child resized" event/subscription mechanism for what's currently a single
    /// caller.
    /// </remarks>
    internal void MeasureAndArrange()
    {
        Vector2 availableSize;
        if (_parent == null)
        {
            // Root element: guaranteed to have a maximum set (from Build) already sitting
            // in _geometry.MaximumSize.
            availableSize = _geometry.MaximumSize;
        }
        else
        {
            // Per axis: a scrollable parent (see AddChildWindow/RecalculateScrollBoundsFromChildren)
            // deliberately lets its children exceed its own content size on that axis -- overflow
            // is meant to be revealed by scrolling, not clamped away -- so on that axis the child's
            // own already-configured _geometry.MaximumSize (its Layout.MaximumSize option, e.g. a
            // generous sentinel like SelectionWindowContent's UnboundedChildHeight) is the real
            // constraint instead of the parent's visible content size. Without this, a scrollable
            // parent's children were silently reclamped to the parent's actual (small) content
            // size on every Measure regardless of what Layout.MaximumSize asked for, since this
            // constructor-time value gets unconditionally overwritten below.
            var parentAvailableSize = _parent.ContentSize - _geometry.RelativePosition;
            availableSize = new Vector2(
                _parent.CanUserScrollHorizontal
                    ? _geometry.MaximumSize.X
                    : parentAvailableSize.X,
                _parent.CanUserScrollVertical
                    ? _geometry.MaximumSize.Y
                    : parentAvailableSize.Y);
        }

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

        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            MeasureChildren(availableSize);
            RecalculateWrapContentSize();
        }
        else
        {
            switch (_geometry.DisplayMode)
            {
                case ElementDisplayMode.Minimized:
                    RecalculateMinimizedSize();
                    break;
                case ElementDisplayMode.Fixed:
                    RecalculateFixedSize();
                    break;
                case ElementDisplayMode.Fill:
                    RecalculateFillSize();
                    break;
                default:
                    throw new NotImplementedException("No default display mode.");
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
        foreach (var childElement in _children)
        {
            childElement.Measure(availableContentSize - childElement.RelativePosition);
        }
    }

    /// <summary>
    /// Top-down absolute-position/rectangle assignment, run only after every node in this
    /// subtree has already been measured (see Measure) -- RecalculateRectangles and
    /// RecalculateHeaderExtras both read sizes that must already be final.
    /// </summary>
    protected void Arrange()
    {
        RecalculateAbsolutePositions();
        RecalculateRectangles();
        RecalculateHeaderExtras();

        foreach (var childElement in _children)
        {
            childElement.Arrange();
        }
    }

    private void RecalculateAbsolutePositions()
    {
        var previousAbsolutePosition = _geometry.AbsolutePosition;

        // A child's relative position is relative to the parent's content area, not the
        // parent's outer  bounds -- matching Build's own initial computation
        // below.
        _geometry.AbsolutePosition = _parent != null
            ? _parent.ContentAbsolutePosition + _geometry.RelativePosition - _parent.ScrollOffset
            : _geometry.RelativePosition;

        _headerState.AbsolutePosition = _geometry.AbsolutePosition + BorderInset;

        _contentState.AbsolutePosition = new Vector2(
            _geometry.AbsolutePosition.X + BorderInset.X,
            _geometry.AbsolutePosition.Y + BorderInset.Y + HeaderInsetHeight);

        if (_geometry.AbsolutePosition != previousAbsolutePosition)
        {
            Moved?.Invoke(this);
        }
    }

    /// <summary>
    /// Collapsed size when Minimized -- shrinks content to zero and clamps to whatever
    /// _header.Size currently holds (already set by the subclass's own Buil, e.g. a
    /// text-measured title height for Window or a fixed icon size for Folder). Window overrides
    /// this to actively re-measure its title text's width (see Window.RecalculateMinimizedWindowSize),
    /// since title text can change after construction (SetText); this generic default doesn't
    /// re-measure anything, just re-clamps the header's last-known size.
    /// </summary>
    protected virtual void RecalculateMinimizedSize()
    {
        _contentState.Size = Vector2.Zero;

        var windowSize = _headerState.Size + BorderInsetDoubled;

        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(windowSize.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X),
            windowSize.Y);
    }

    protected virtual void RecalculateFixedSize()
    {
        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(_geometry.OriginalSize.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X),
            MathHelper.Clamp(_geometry.OriginalSize.Y, _geometry.MinimumSize.Y, _geometry.MaximumSize.Y));

        // Resize horizontally to fit the new element's size, but keep the vertical size.
        _headerState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _headerState.OriginalSize.Y - BorderInset.Y);

        // Content sits below the header (which itself already accounts for just the top
        // border) and above the bottom border, so its own height must clear both the top and
        // bottom border -- BorderInsetDoubled.Y, not BorderInset.Y. Getting this wrong left no
        // room for a bottom border strip, so content's own background fill (drawn after the
        // border in Draw()) painted directly over it.
        _contentState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _geometry.CurrentSize.Y - BorderInsetDoubled.Y - HeaderInsetHeight);
    }

    protected virtual void RecalculateFillSize()
    {
        _geometry.CurrentSize = _geometry.MaximumSize;

        _headerState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _headerState.OriginalSize.Y - BorderInset.Y);

        _contentState.Size = _geometry.CurrentSize
            - (_headerState.ShowHeader
                ? _headerState.Size
                : Vector2.Zero)
            - BorderInsetDoubled;
    }

    protected virtual void RecalculateWrapContentSize()
    {
        if (_canContainChildren && _children.Count > 0)
        {
            // Relative position + measured size, not the absolute Rectangle -- this
            // runs during Measure, before children have been Arranged, so their absolute
            // position isn't valid yet. Also more correct in general: fitting to children
            // shouldn't need absolute screen coordinates at all.
            var maxRight = 0f;
            var maxBottom = 0f;
            foreach (var child in _children)
            {
                maxRight = System.Math.Max(maxRight, child.RelativePosition.X + child.CurrentSize.X);
                maxBottom = System.Math.Max(maxBottom, child.RelativePosition.Y + child.CurrentSize.Y);
            }
            _contentState.Size = new Vector2(maxRight, maxBottom);
        }
        else
        {
            _contentState.Size = new Vector2(0, 0);
        }

        _contentState.Size += ContentPadding;

        // A WrapContent element must be at least as wide as its header needs (e.g. Window's
        // title text + buttons -- see MinimumHeaderWidth), not just its content.
        if (_headerState.ShowHeader)
        {
            _contentState.Size.X = System.Math.Max(_contentState.Size.X, MinimumHeaderWidth());
        }

        _geometry.CurrentSize = _contentState.Size;
        if (_headerState.ShowHeader)
        {
            _headerState.Size = new Vector2(_contentState.Size.X, _headerState.OriginalSize.Y - BorderInset.Y);
            _geometry.CurrentSize.Y += _headerState.Size.Y;
        }
        _geometry.CurrentSize += BorderInsetDoubled;
    }

    /// <summary>Recalculates draw rectangles from the current absolute positions/sizes.</summary>
    private void RecalculateRectangles()
    {
        _geometry.Rectangle = new Rectangle((int)_geometry.AbsolutePosition.X, (int)_geometry.AbsolutePosition.Y, (int)_geometry.CurrentSize.X, (int)_geometry.CurrentSize.Y);
        _headerState.Rectangle = new Rectangle((int)_headerState.AbsolutePosition.X, (int)_headerState.AbsolutePosition.Y, (int)_headerState.Size.X, (int)_headerState.Size.Y);
        _contentState.Rectangle = new Rectangle((int)_contentState.AbsolutePosition.X, (int)_contentState.AbsolutePosition.Y, (int)_contentState.Size.X, (int)_contentState.Size.Y);
        _viewport = new Viewport(_contentState.Rectangle);

        RecalculateBorderRectangles();
    }

    private void RecalculateBorderRectangles()
    {
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(_geometry.Rectangle, _border.Thickness);
        _border.TopRectangle = top;
        _border.BottomRectangle = bottom;
        _border.LeftRectangle = left;
        _border.RightRectangle = right;
    }

    public void SetIsVisible(bool isVisible)
    {
        _isVisible = isVisible;
        _parent?.MeasureAndArrange();
    }

    internal void SetFocused(bool isFocused)
    {
        if (_isFocused == isFocused)
        {
            return;
        }

        _isFocused = isFocused;
        FocusChanged?.Invoke(this);
    }

    public void SetDisplayMode(ElementDisplayMode newDisplayMode)
    {
        if (newDisplayMode == _geometry.DisplayMode)
        {
            return;
        }

        _geometry.PreviousDisplayMode = _geometry.DisplayMode;
        _geometry.DisplayMode = newDisplayMode;
        MeasureAndArrange();
        DisplayModeChanged?.Invoke(this);
    }

    /// <summary>
    /// Repositions the element relative to its parent (or the screen, for a root element).
    /// For chrome behaviors that need to move an element --
    /// Resized/Moved fire automatically through MeasureAndArrange.
    /// </summary>
    public void SetRelativePosition(Vector2 relativePosition)
    {
        _geometry.RelativePosition = relativePosition;
        MeasureAndArrange();
    }

    /// <summary>
    /// Sets the Fixed-mode size. Only display mode Fixed reads this size --
    /// Fill/WrapContent compute size from the parent/content instead, so this method has no
    /// visible effect on a Fill or WrapContent element, matching that an element can't be
    /// manually resized while it's set to auto-size.
    /// </summary>
    public void SetSize(Vector2 size)
    {
        _geometry.OriginalSize = size;
        MeasureAndArrange();
    }

    /// <summary>
    /// Sets relative position and Fixed-mode size together in one MeasureAndArrange pass --
    /// needed for left/top-edge resize, which must move position and
    /// size together to keep the opposite edge visually fixed. A separate SetSize then
    /// SetRelativePosition would relayout twice and fire Resized then Moved from two calls
    /// instead of one.
    /// </summary>
    internal void SetBounds(Vector2 relativePosition, Vector2 size)
    {
        _geometry.RelativePosition = relativePosition;
        _geometry.OriginalSize = size;
        MeasureAndArrange();
    }

    public void Close()
    {
        Closed?.Invoke(this);
        _elementPoolService.CloseElement(this);
    }
}
