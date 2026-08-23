using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

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

    /// <summary>Internal, not protected: chrome behaviors (see IChromeBehavior) live outside the Element/Window hierarchy but still need a host window's FontService/ElementPoolService to build a title Button through the normal pooled ElementPoolService.CreateElement path.</summary>
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

    /// <summary>Raised by a window that can't move focus itself (e.g. a TextBox submitting via Enter) to ask UiInputController to move it elsewhere -- see UiInputController.SetFocus, which subscribes/unsubscribes this the same way it does Closed.</summary>
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

    /// <summary>Whether this element can be found by hit-testing (FindTopmostHit/TryHitTestInteraction) at all -- defaults to just IsVisible; Button overrides this to also require Enabled, so a disabled button is never hovered, pressed, or clicked, rather than every caller having to remember to check Enabled itself.</summary>
    protected virtual bool IsHitTestable => _isVisible;

    /// <summary>
    /// Free-form consumer-defined association, the same role WPF's FrameworkElement.Tag or
    /// WinForms' Control.Tag plays -- lets a caller mark "this element belongs to/represents X"
    /// without X needing to be this element's own IElementContent (see InventoryGridContent's own
    /// doc comment for the concrete motivating case: a host window whose InventoryGridContent is
    /// driven manually, never assigned via SetContent, so Window.Content alone can't identify it).
    /// Reset to null on Build (see its own doc comment on pooled reuse) so a reused instance never
    /// inherits a stale reference from whatever it was last used for.
    /// </summary>
    public object? Tag { get; set; }

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

    /// <summary>True while this window holds input focus -- set by UiInputController, not this window itself.</summary>
    public bool IsFocused => _isFocused;

    /*========Header========*/
    /// <summary>Internal, not protected, for the same reason as WindowService/FontService -- Button uses this to center its label the same way LabelRenderer centers map tile glyphs.</summary>
    internal LabelRenderer LabelRenderer { get; }

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

    /// <summary>Draws this element's header region -- Window's title bar (background/text/buttons) and Folder's icon are both just their own override of this, called from Draw whenever the header is shown (see the ShowHeader/ShowHeaderWhenMinimized gate there). No-op by default. Overrides needing SpriteBatch/Texture2D read them from ElementPoolService (see its own doc comment) rather than taking them as parameters.</summary>
    protected virtual void DrawHeader(GameTime gameTime) { }

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
    public BorderStyle BorderStyle { get => _border.Style; set => _border.Style = value; }
    public Color BorderColor => _border.Color;

    /*========Content========*/
    /// <summary>Content-area bookkeeping -- see ElementGeometryState's doc comment for the same "grouped, plain fields" rationale. Named _contentState, not _content, to avoid colliding with the name of Window's own pluggable IElementContent field.</summary>
    private protected readonly ElementContentState _contentState = new();

    public Vector2 ContentAbsolutePosition => _contentState.AbsolutePosition;
    public Vector2 ContentSize => _contentState.Size;
    public Rectangle ContentRectangle => _contentState.Rectangle;
    public Vector2 ContentPadding { get; set; } = new(5, 5);
    public Color ContentColor => _contentState.BackgroundColor;

    /// <summary>Changes the content background color after creation -- ElementContentOptions.ContentColor only ever sets it once, at CreateElement time; this is for a control that needs its own background to react to later state changes (e.g. GridControl's toggle buttons tinting differently once on).</summary>
    public void SetContentColor(Color color) => _contentState.BackgroundColor = color;

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

    public Element(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    {
        ArgumentNullException.ThrowIfNull(fontService);
        ArgumentNullException.ThrowIfNull(elementPoolService);
        ArgumentNullException.ThrowIfNull(labelRenderer);

        FontService = fontService;
        LabelRenderer = labelRenderer;
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

        // Pooled elements must not inherit a stale Tag reference from whatever they were last
        // used for -- see Tag's own doc comment.
        Tag = null;

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
        _border.Color = chrome?.BorderColor ?? WindowPalette.BorderColor;

        /*========Content========*/
        _contentState.BackgroundColor = content?.ContentColor ?? WindowPalette.BodyColor;

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
        // Skipped when the parent is currently Minimized (collapsed, deliberately zero content
        // area, see RecalculateMinimizedSize) -- there is nothing real to measure against yet.
        // This element just keeps its Build-time CurrentSize/RelativePosition until the parent's
        // own next real (non-Minimized) Measure/Arrange pass reaches it -- see Measure's own
        // matching Minimized guard, which is what actually performs that later remeasure once
        // the parent expands. Root elements (no parent) are unaffected -- MeasureAndArrange's
        // own root branch never depends on a parent's state.
        if (_parent is null || _parent.DisplayMode != ElementDisplayMode.Minimized)
        {
            MeasureAndArrange();
        }

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
            // Gated here, at the caller, rather than inside Update() itself -- several
            // subclasses (Window, AbilityScoreWindow, MapWindow) call base.Update() and then do
            // their own additional work afterward, which an early-return guard at the top of
            // this base method couldn't prevent. Skipping the call entirely, before it's ever
            // made, is airtight regardless of what an override's own Update body does.
            //
            // Root elements are deliberately NOT covered by this -- see
            // ShellContext.UpdateWindowLayer, which still calls Update on every root window
            // regardless of IsVisible. This codebase's persistent Tooltip popups (AbilityScoreWindow's,
            // InventoryFolderController's, HotbarController's Armed Hotkey Summary) are toggled
            // via IsVisible while remaining in UiLayer.Tooltip's root list, but are driven
            // entirely externally now (whatever owns the hover state calls ShowNear/Hide
            // directly) -- their own Update is a harmless no-op regardless of visibility, not a
            // reason root elements need to keep ticking while hidden. The exclusion is kept
            // anyway as the conservative default: a genuine parent/child relationship (this loop)
            // never has a self-polling shape (a hidden child is driven by whatever explicitly
            // toggled it), so excluding it from Update is safe there specifically; a root
            // element's own Update contract isn't guaranteed the same way, so this doesn't assume
            // every current or future root element is equally safe to skip.
            if (!childElement.IsVisible)
            {
                continue;
            }

            childElement.Update(gameTime);
        }
    }

    /// <summary>Reads GraphicsDevice/SpriteBatch/UnitRectangle from ElementPoolService (see its own doc comment for why this Element doesn't cache its own copies) rather than taking them as parameters -- every override, and every recursive child Draw call below, does the same.</summary>
    public virtual void Draw(GameTime gameTime)
    {
        if (!_isVisible)
        {
            return;
        }

        var graphicsDevice = _elementPoolService.GraphicsDevice;
        var spriteBatch = _elementPoolService.SpriteBatch;
        var unitRectangle = _elementPoolService.UnitRectangle;

        if (_border.Show)
        {
            BorderRenderer.Draw(spriteBatch, unitRectangle, _border.Style, _border.Color, _border.TopRectangle, _border.BottomRectangle, _border.LeftRectangle, _border.RightRectangle);
        }

        if (_isGlowing)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, _geometry.Rectangle, _glowColor);
        }

        if ((_geometry.DisplayMode != ElementDisplayMode.Minimized && _headerState.ShowHeader) || (_geometry.DisplayMode == ElementDisplayMode.Minimized && _headerState.ShowHeaderWhenMinimized))
        {
            DrawHeader(gameTime);
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

                DrawContent(gameTime);

                spriteBatch.End();
                graphicsDevice.Viewport = previousViewport;
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            }
            else
            {
                DrawContent(gameTime);
            }

            if (RequiresContentViewport)
            {
                // Unlike the viewport swap above (which only ever wraps this element's own
                // DrawContent), children draw in absolute screen coordinates, not local ones, so
                // they need an actual scissor clip rather than a viewport/transform remap -- a
                // child scrolled toward negative local space (see ScrollBy's own doc comment: it
                // only ever folds ScrollOffset into children's position math, nothing clips their
                // drawing) would otherwise render in full whichever screen position that puts it
                // at, however far outside this element's own bounds that is. Confirmed bug: the
                // Inventory tab strip's tiles rendered past the window's left border while
                // scrolling instead of disappearing off the edge. Intersected with whatever
                // scissor is already active (harmless today -- nothing nests scrollable elements
                // yet -- but correct if that ever changes) rather than overwritten outright.
                spriteBatch.End();

                var previousScissorRectangle = graphicsDevice.ScissorRectangle;
                graphicsDevice.ScissorRectangle = Rectangle.Intersect(previousScissorRectangle, _contentState.Rectangle);
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, ScissorClipRasterizerState);

                foreach (var childElement in _children)
                {
                    childElement.Draw(gameTime);
                }

                spriteBatch.End();
                graphicsDevice.ScissorRectangle = previousScissorRectangle;
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            }
            else
            {
                foreach (var childElement in _children)
                {
                    childElement.Draw(gameTime);
                }
            }
        }
    }

    /// <summary>Shared by every scrollable element's own children-clip pass in Draw -- ScissorTestEnable is off by default on every other RasterizerState this codebase uses, so this needs to be its own instance rather than a tweaked copy of an existing one.</summary>
    private static readonly RasterizerState ScissorClipRasterizerState = new() { ScissorTestEnable = true };

    /// <summary>No-op by default; TextWindow/MapWindow override this directly, Window overrides it to host IElementContent. Overrides needing SpriteBatch/Texture2D read them from ElementPoolService (see its own doc comment) rather than taking them as parameters.</summary>
    public virtual void DrawContent(GameTime gameTime) { }

    /// <summary>Routes a key newly pressed this frame to this window while it holds focus -- see UiInputController.RouteKeyPressesToFocusedWindow.</summary>
    internal void HandleKeyPress(Keys key) => OnKeyPressAction(key);

    protected virtual void OnKeyPressAction(Keys key) { }

    /// <summary>
    /// Routes the whole keyboard state to this window once per frame while it holds focus --
    /// see UiInputController.RouteHotkeysToFocusedWindow. Unlike HandleKeyPress (one discrete
    /// key-press event at a time), this is for windows whose own hotkeys need continuous or
    /// combined multi-key state (e.g. MapWindow's WASD scroll, which reads all four keys'
    /// current down-state together rather than reacting to one press event).
    /// </summary>
    internal void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => OnHotkeysAction(keyboardState, previousKeyboardState);

    protected virtual void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState) { }

    /// <summary>Shared "newly pressed this frame" edge-detection for OnHotkeysAction overrides and UiInputController's own Tab handling.</summary>
    internal static bool WasKeyPressed(KeyboardState current, KeyboardState previous, Keys key) => current.IsKeyDown(key) && previous.IsKeyUp(key);

    /// <summary>
    /// Fires once when a right-mouse-button drag starts over this window -- see
    /// UiInputController's right-button state machine. No-op by default; MapWindow uses
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
    /// started over -- see UiInputController's right-button state machine and
    /// HandleRightDragStart. No-op by default (only MapWindow overrides this today, to pan its
    /// camera); unlike HandleHotkeys this doesn't depend on focus, since a drag-to-pan gesture
    /// shouldn't require clicking to focus a window first.
    /// </summary>
    internal void HandleRightDrag(Vector2 totalPixelDeltaSinceStart) => OnRightDragAction(totalPixelDeltaSinceStart);

    protected virtual void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) { }

    /// <summary>
    /// Fires once when an in-progress right-mouse-button drag ends (button released) -- see
    /// UiInputController's right-button state machine. No-op by default; MapWindow uses this
    /// to settle its smooth sub-tile scroll offset onto the tile grid once the gesture is over,
    /// rather than mid-drag on every frame.
    /// </summary>
    internal void HandleRightDragEnd() => OnRightDragEndAction();

    protected virtual void OnRightDragEndAction() { }

    /// <summary>
    /// Fires instead of HandleRightDragEnd when a right-button press/release never moved the
    /// mouse past UiInputController's small tap-vs-drag pixel threshold -- a right-click
    /// "tap," distinct from the drag-to-pan gesture the same button also drives. No-op by
    /// default; MapWindow uses this to cancel an armed ability (and, if nothing was armed, open
    /// a corpse's context menu), TextBox to open its own Cut/Copy/Paste/Select All menu -- both
    /// take position rather than self-polling Mouse.GetState(), since UiInputController already
    /// has the authoritative release position that decided this was a tap in the first place.
    /// </summary>
    internal void HandleRightClickTap(Point position) => OnRightClickTapAction(position);

    /// <summary>
    /// Settable per-instance right-click handler -- lets any Element opt into a right-click
    /// context menu without its own OnRightClickTapAction override, the same settable-delegate
    /// shape MapWindow.OnCorpseClicked/IsTextInputFocused already use for late-bound, externally-
    /// wired behavior (the owning controller, not this Element itself, usually knows what the
    /// menu should contain -- e.g. DynamicHudContextMenus' Close/Close All for a window,
    /// InventoryGridContent's Give/Take for a cell). The base OnRightClickTapAction below just
    /// invokes this; a subclass with genuinely custom right-click logic (MapWindow, TextBox)
    /// still overrides OnRightClickTapAction directly instead and never touches this field.
    /// </summary>
    public Action<Point>? OnRightClicked { get; set; }

    protected virtual void OnRightClickTapAction(Point position) => OnRightClicked?.Invoke(position);

    /// <summary>
    /// Fires on every root/HUD window when Escape is pressed -- see
    /// UiInputController.HandleEscape for why this is broadcast unconditionally rather than
    /// routed only to whichever window holds focus. No-op by default; MapWindow uses this to
    /// cancel an armed ability or an in-progress Delayed action windup, the same cancellation
    /// OnRightClickTapAction triggers.
    /// </summary>
    internal void HandleEscape() => OnEscapeAction();

    protected virtual void OnEscapeAction() { }

    /// <summary>
    /// Routes an actual typed character (shifted case, punctuation, OS keyboard layout) to
    /// this window while it holds focus -- see UiInputController.RouteTextInputToFocusedWindow.
    /// Neither HandleKeyPress (raw Keys values) nor HandleHotkeys (modifier-aware combos) can
    /// deliver real characters; this is fed from FNA's TextInputEXT.
    /// </summary>
    internal void HandleTextInput(char character) => OnTextInputAction(character);

    protected virtual void OnTextInputAction(char character) { }

    /// <summary>
    /// Finds the next focusable (CanUserFocus) element among this window's direct children,
    /// starting right after `after` and wrapping around (after: null means "the first one").
    /// Shared by TextBox's own Enter-to-advance and UiInputController.SetFocus's auto-redirect
    /// into a container's first focusable child -- both are "find the next focusable sibling,"
    /// the second just with after: null. Generic over CanUserFocus rather than hardcoded to
    /// TextBox specifically -- TextBox is the only focusable leaf type that exists today, but
    /// CanUserFocus is already how every current call site opts a non-focusable child out (every
    /// GridControl/TabbedContent/AbilityScoreWindow tile, button, and non-editable window
    /// explicitly sets CanUserFocus = false), so any future focusable widget (e.g. a dropdown or
    /// list selector) is picked up automatically without this method needing to know its type.
    /// </summary>
    internal Element? NextFocusableDescendant(Element? after)
    {
        // after: null starts the scan at index 0 (IndexOf(null) is -1, so +1 lands on 0) and
        // never matches the candidate == after break check below, since _childElements never
        // contains a null entry -- the scan just runs to completion in order, i.e. "the first
        // one." after: some child starts right after it and wraps around, stopping once the
        // scan loops all the way back to after itself (a lone focusable child must not "advance"
        // to itself) rather than re-checking it as index Count.
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
            if (candidate.IsVisible && candidate.CanUserFocus)
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

    /// <summary>
    /// Topmost (last-added first) hit-testable member of container whose Rectangle contains
    /// position, or null. The one hit-test algorithm shared by both the header zone (Window's
    /// FindHeaderButtonAt, scoped to _titleButtons) and the content zone (OnContentClickAction's
    /// own default below, scoped to _children) -- those two zones are a real, load-bearing split
    /// (header always wins a click over starting a drag, see TryHitTestInteraction), but there's
    /// no reason the code that walks either one should be written twice.
    /// </summary>
    private protected static Element? FindTopmostHit(IReadOnlyList<Element> container, Point position)
    {
        for (var index = container.Count - 1; index >= 0; index--)
        {
            var candidate = container[index];
            if (candidate.IsHitTestable && candidate.Rectangle.Contains(position))
            {
                return candidate;
            }
        }

        return null;
    }

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
        // An invisible element (and, since this recurses, its whole subtree) is never a valid
        // interaction target -- matching WPF's Visibility.Hidden/Collapsed, both of which stop
        // hit-testing, unlike this codebase's previous behavior where hiding something only
        // stopped it from being drawn, not from being clicked/dragged/dropped onto.
        if (!IsHitTestable || !_geometry.Rectangle.Contains(position))
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

        // A content-area Button is reached here (as a leaf, via the children loop just above
        // recursing into its own TryHitTestInteraction) rather than through FindHeaderButtonAt --
        // still needs the same ButtonClick shape a header button gets, so UiInputController's
        // PressedButton/HoveredButton tracking covers a button anywhere, not just the title bar.
        return this is Button self
            ? ElementInteraction.ButtonClick(this, self)
            : ElementInteraction.Click(this);
    }

    /// <summary>
    /// Moves this window to the end of its parent's child list, so it draws last (on top) and
    /// wins future overlapping hit-tests against its siblings. No-op for a root window (no
    /// parent) -- UiInputController is responsible for raising a root window to the front of
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

    /// <summary>
    /// Routes a content click to the topmost child whose Rectangle contains it, then stops --
    /// the same topmost-first, single-target philosophy TryHitTestInteraction already uses for
    /// drags, now applied to plain clicks too (and, like TryHitTestInteraction, skipping
    /// non-hit-testable children -- see FindTopmostHit). Previously looped every overlapping child in list (not z-) order
    /// with no way to stop -- harmless for the common non-overlapping-sibling case, but wrong
    /// the moment two children's Rectangles genuinely overlapped a point (e.g. Floating mode),
    /// which fired HandleClick on every one of them instead of just the topmost one the user
    /// actually sees and clicked -- the same "who owns this point" bug TryHitTestInteraction was
    /// already written to avoid for drags.
    /// </summary>
    protected virtual void OnContentClickAction(Point mousePosition) => FindTopmostHit(_children, mousePosition)?.HandleClick(mousePosition);

    /// <summary>
    /// Attaches newChild to this element and initializes it -- callers must NOT also call
    /// newChild.Initialize() themselves afterward (confirmed bug, found via a live-testing stack
    /// trace: several call sites called AddChild followed by an explicit Initialize(), running it
    /// twice on the same instance -- catastrophic for a Window subclass whose own Initialize()
    /// itself calls AddChild for its own children, since the second pass created a second,
    /// orphaned set of them). Positioning is handled separately, by Arrange (see RetileChildren)
    /// rather than here, so it's correct regardless of when in this element's lifecycle newChild
    /// was added -- see RetileChildren's own doc comment for why that split matters.
    /// </summary>
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

        newChild.Initialize();

        // Repositions every Horizontal/Vertical-tiled child (not just newChild) from their
        // current, now-final CurrentSize -- see RetileChildren's own doc comment. Cheap: no
        // remeasuring, just the same position-chaining arithmetic Arrange already does on every
        // layout pass.
        Arrange();

        // A WrapContent parent's own size depends on its children's -- re-fit around the
        // newly added child. Gated to WrapContent only: for Fixed/Fill/Minimized parents the
        // Arrange call above already repositions every affected child, and the parent's own
        // size never depends on children in those modes, so an unconditional call here would
        // just re-walk the entire existing sibling list for no effect. Deferred to the end of
        // the batch (see BeginLayoutBatch) if one is open, rather than re-fitting after every
        // single AddChild in a loop that's about to add several more.
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            RefitWrapContentSizeNowOrDeferToLayoutBatch();
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

    /// <summary>Removes the child, then retiles the remaining ones so later siblings close the gap instead of keeping the removed window's slot as dead space.</summary>
    public void RemoveChild(Guid elementId)
    {
        var childElementIndex = _children.FindIndex(childElement => childElement.ElementId == elementId);
        if (childElementIndex < 0)
        {
            return;
        }

        var removedChild = _children[childElementIndex];
        _children.RemoveAt(childElementIndex);

        // Repositions every remaining Horizontal/Vertical-tiled child to close the gap -- see
        // RetileChildren's own doc comment. Never touches Initialize -- every remaining sibling
        // here is already live; only its position may need recomputing.
        Arrange();

        // See the matching comment in AddChild -- a WrapContent parent needs to shrink to fit
        // around the removed child; other modes don't depend on children for sizing.
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            RefitWrapContentSizeNowOrDeferToLayoutBatch();
        }

        if (CanUserScrollVertical || CanUserScrollHorizontal)
        {
            removedChild.Resized -= OnChildElementResizedForScrollBounds;
            RecalculateScrollBoundsFromChildren();
        }
    }

    /// <summary>Depth of nested BeginLayoutBatch scopes currently open on this element -- 0 means none.</summary>
    private int _layoutBatchDepth;

    /// <summary>Set by AddChild/RemoveChild when a WrapContent re-fit was suppressed because a layout batch was open -- tells the outermost scope's Dispose to actually perform the deferred MeasureAndArrange once, rather than unconditionally running one even when nothing was actually added/removed inside the batch.</summary>
    private bool _layoutBatchNeedsMeasureAndArrange;

    private void RefitWrapContentSizeNowOrDeferToLayoutBatch()
    {
        if (_layoutBatchDepth > 0)
        {
            _layoutBatchNeedsMeasureAndArrange = true;
        }
        else
        {
            MeasureAndArrange();
        }
    }

    /// <summary>
    /// Suppresses the per-AddChild/RemoveChild WrapContent re-fit while multiple children are
    /// added or removed in a loop, coalescing what would otherwise be one full subtree Measure/
    /// Arrange pass per call into a single pass when the returned scope is disposed -- the same
    /// problem SetBounds already solves for the narrower SetSize+SetRelativePosition case,
    /// generalized to any number of AddChild/RemoveChild calls (e.g. NotificationCenter building
    /// one count window per category, InventoryFolderController building its tiles). Reentrant:
    /// nested batches on the same element collapse into the outermost one, so a helper method
    /// that opens its own batch still composes correctly when called from within a caller's
    /// batch. Opt-in rather than automatic -- every AddChild/RemoveChild call outside a batch
    /// behaves exactly as before, so nothing that currently reads geometry synchronously right
    /// after a mutation is affected unless it explicitly opts into batching.
    ///
    /// Does not defer the position-only Arrange in AddChild/RemoveChild (see RetileChildren) --
    /// that's cheap (no remeasuring) and keeping it immediate means a child's own position is
    /// always correct immediately after AddChild returns, batch or not; only the WrapContent
    /// size refit (the actually expensive full remeasure) is deferred.
    /// </summary>
    public IDisposable BeginLayoutBatch()
    {
        _layoutBatchDepth++;
        return new LayoutBatchScope(this);
    }

    private sealed class LayoutBatchScope(Element element) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            element._layoutBatchDepth--;

            if (element._layoutBatchDepth == 0 && element._layoutBatchNeedsMeasureAndArrange)
            {
                element._layoutBatchNeedsMeasureAndArrange = false;
                element.MeasureAndArrange();
            }
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
            if (!childElement.IsVisible)
            {
                continue;
            }

            maxRight = System.Math.Max(maxRight, childElement.RelativePosition.X + childElement.CurrentSize.X);
            maxBottom = System.Math.Max(maxBottom, childElement.RelativePosition.Y + childElement.CurrentSize.Y);
        }

        SetMaxScrollOffset(new Vector2(maxRight - _contentState.Size.X, maxBottom - _contentState.Size.Y));
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
        else if (_geometry.DisplayMode == ElementDisplayMode.Minimized)
        {
            // No MeasureChildren here -- a collapsed element has no real content area to
            // measure children against (RecalculateMinimizedSize deliberately zeroes
            // ContentSize), and re-measuring them against that zero region would clamp a
            // Fixed-mode child down to nothing, permanently corrupting it and, via
            // RetileChildren's position chaining, every later Vertical/Horizontal sibling too
            // (confirmed by a live regression when this guard was missing). Children simply
            // keep whatever size/position they already had -- Element.Initialize's own matching
            // guard means a child added while its parent is Minimized never had a real
            // measurement in the first place, so it just keeps its Build-time OriginalSize until
            // this element's next real (non-Minimized) Measure/Arrange pass reaches it.
            RecalculateMinimizedSize();
        }
        else
        {
            switch (_geometry.DisplayMode)
            {
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
            // An invisible child is excluded from measurement entirely -- the same
            // Visibility.Collapsed/View.GONE treatment RecalculateWrapContentSize and
            // RetileChildren give it, not just skipped when sizing this element around its
            // children. Becoming visible again re-triggers a full MeasureAndArrange via
            // SetIsVisible, so nothing is permanently stale once it reappears.
            if (!childElement.IsVisible)
            {
                continue;
            }

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

        RetileChildren();

        foreach (var childElement in _children)
        {
            if (!childElement.IsVisible)
            {
                continue;
            }

            childElement.Arrange();
        }
    }

    /// <summary>
    /// Recomputes RelativePosition for every child from ChildElementTileMode, purely from each
    /// child's current CurrentSize -- Horizontal/Vertical chain each child off the previous
    /// sibling's already-Measured position+size; Floating leaves position alone entirely (the
    /// creator owns it). Run on every Arrange pass rather than only when a child is added or
    /// removed -- the same way a real layout engine's stack/linear panel (WPF's StackPanel,
    /// Android's LinearLayout) recomputes every child's offset on every layout pass instead of
    /// baking positions in once at insertion time.
    ///
    /// This replaces the old RetileChildrenFrom, which ran only from AddChild/RemoveChild and
    /// only from the mutated index onward -- baking each child's position in once, from whatever
    /// CurrentSize its siblings happened to have at that exact moment. That broke the moment a
    /// child was added to a Vertical/Horizontal-tiled parent (e.g. Folder) while the parent was
    /// Minimized: Measure's own Minimized guard leaves a not-yet-measured child at its Build-time
    /// OriginalSize, but nothing ever re-chained positions once the parent later expanded and
    /// remeasured children for real -- every sibling positioned during that degenerate window
    /// stayed corrupted forever. Recomputing fully, every Arrange pass, from whatever CurrentSize
    /// each child currently holds (always correct by the time Arrange runs, since Measure always
    /// precedes Arrange in MeasureAndArrange) makes this immune to when in an element's lifecycle
    /// a child was added -- before this element's own first Initialize, after it while collapsed,
    /// or after it while expanded all converge on the same correct layout.
    /// </summary>
    private void RetileChildren()
    {
        if (_childrenTileMode == ChildElementTileMode.Floating)
        {
            return;
        }

        // An invisible child neither occupies tiling space nor advances the layout cursor for
        // later siblings -- the same "collapsed elements are skipped by layout" semantics WPF's
        // Visibility.Collapsed and Android's View.GONE use, chained off the last VISIBLE sibling
        // instead of strictly the previous index.
        Element? previousVisibleChild = null;

        foreach (var childElement in _children)
        {
            if (!childElement.IsVisible)
            {
                continue;
            }

            childElement._geometry.RelativePosition = previousVisibleChild is null
                ? new Vector2(0, 0)
                : _childrenTileMode == ChildElementTileMode.Horizontal
                    ? new Vector2(
                        previousVisibleChild._geometry.RelativePosition.X + previousVisibleChild._geometry.CurrentSize.X,
                        previousVisibleChild._geometry.RelativePosition.Y)
                    : new Vector2(
                        previousVisibleChild._geometry.RelativePosition.X,
                        previousVisibleChild._geometry.RelativePosition.Y + previousVisibleChild._geometry.CurrentSize.Y);

            previousVisibleChild = childElement;
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
                if (!child.IsVisible)
                {
                    continue;
                }

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
    /// <summary>
    /// Derives every Rectangle (this element's own, its header's, its content's, its border
    /// edges) purely from whatever AbsolutePosition/CurrentSize/header-and-content Size are
    /// already sitting in _geometry/_headerState/_contentState -- pure geometry, agnostic to how
    /// those absolute positions were arrived at. Private protected (not private) so Button's own
    /// PositionInHeader can reuse it too: a title button's AbsolutePosition comes from an
    /// explicitly-supplied header host position rather than a parent (see PositionInHeader's own
    /// doc comment for why), but once that's set, deriving its Rectangles is identical work --
    /// no reason to duplicate this math a second time for that one case.
    /// </summary>
    private protected void RecalculateRectangles()
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
    /// Sets MinimumSize after construction -- Build only ever sets it once, from
    /// ElementOptions.Layout.MinimumSize (see its own doc comment), which doesn't work for a
    /// Fixed-mode window computing its own natural (content-driven) size at runtime rather than
    /// knowing it upfront at CreateElement time. Does not itself trigger a re-measure -- call
    /// SetSize afterward if the current size also needs to move up to the new floor.
    /// </summary>
    public void SetMinimumSize(Vector2 minimumSize) => _geometry.MinimumSize = minimumSize;

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

    /// <summary>
    /// Extension point for a subclass's own pluggable/swappable state that neither Build (which
    /// only resets what ElementOptions itself covers) nor ClearEventSubscriptions (events only)
    /// touches -- e.g. Window's own pluggable IElementContent, set via SetContent rather than
    /// through ElementOptions. Called by ElementPoolService.CloseElement for every element in a
    /// close cascade (this element and, via CloseAllChildren, every descendant), always before
    /// the element is actually returned to its pool -- not the same moment as the public Closed
    /// event above, which fires once, only for the element Close() was actually called on, before
    /// CloseElement even runs. No-op by default; override only if a subclass introduces its own
    /// side-channel pluggable reference.
    ///
    /// Deliberately a single targeted hook, not a blanket "reset every field" sweep: Build already
    /// re-initializes ordinary per-use state (position, size, colors, chrome flags) from
    /// ElementOptions on every rent, and a reflective full-field reset would have no way to tell
    /// permanent constructor-captured wiring (fontService, elementPoolService, ...) from genuine
    /// per-use state without per-field annotation -- the same "easy to forget" problem this exists
    /// to solve, just automated. Confirmed need: a Window whose _content was never cleared on
    /// pool-return kept driving its previous life's content (a closed Inventory window's tab-body
    /// Window recycled into InspectionWindowContent's own manual row containers, still silently
    /// updating/rebuilding the old InventoryTabContent every frame) -- see Window's own override.
    ///
    /// Base Element implementation clears OnRightClicked itself -- it's a plain settable
    /// property, not a C# event, so ClearEventSubscriptions' reflection sweep (which only finds
    /// real `event` backing fields) never touches it; left uncleared, a pooled Element reused for
    /// an unrelated purpose would keep invoking whatever handler the *previous* owner wired.
    /// A subclass override must call base.OnClosed() to keep this, the same convention any
    /// override of a hook with real base behavior follows.
    /// </summary>
    protected internal virtual void OnClosed()
    {
        OnRightClicked = null;
    }

    public void Close()
    {
        Closed?.Invoke(this);
        _elementPoolService.CloseElement(this);
    }
}
