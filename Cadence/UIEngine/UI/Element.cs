using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Diagnostics;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

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
public class Element(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
{
    /// <summary>Internal, not protected: chrome behaviors (see IChromeBehavior) live outside the Element/Window hierarchy but still need a host window's FontService/ElementPoolService to build a title Button through the normal pooled ElementPoolService.CreateElement path.</summary>
    public FontService FontService { get; } = fontService;

    /// <summary>See FontService's doc comment -- same reason this is exposed internally.</summary>
    public ElementPoolService ElementPoolService { get; } = elementPoolService;

    /// <summary>What this element draws with: the device, the batches, the unit rectangle and the render-state stack. Public for the same reason as FontService: content an element hosts (IElementContent) draws through its host's.</summary>
    public UiRenderService Render { get; } = renderService;

    public Guid ElementId { get; } = Guid.NewGuid();

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

    /// <summary>Raised when ContentSize actually changes, including when a scrollbar gutter appears or disappears without the element itself resizing.</summary>
    /// <remarks>Fires at the end of Measure, after Resized. A consumer that lays children out from ContentSize (e.g. a grid choosing its column count) should listen to this rather than Resized.</remarks>
    public event Action<Element>? ContentResized;

    /// <summary>Raised when the element's absolute screen position actually changes.</summary>
    public event Action<Element>? Moved;

    /// <summary>
    /// Raised whenever this element's content area is clicked, after OnContentClickAction
    /// (so subclass overrides like TextWindow's own click handling still run
    /// first). Fired from HandleContentClick directly, not from the virtual
    /// OnContentClickAction, so subclasses that override it without calling base still raise
    /// this -- letting external code react to a click without
    /// needing a dedicated Window subclass just to hook one in.
    /// </summary>
    public event Action<Element>? Clicked;

    /// <summary>Raised in place of Clicked for a second click on this element within the double-click window.</summary>
    /// <remarks>
    /// Opt-in: subscribing is what makes UiInputController watch this element's clicks for a pair
    /// (WantsDoubleClickDetection). The first click of the pair still raises Clicked at once -- see
    /// UiInputController.HandleDoubleClickAwareClick.
    /// </remarks>
    public event Action<Element>? DoubleClicked;

    /// <summary>UiInputController's own opt-in signal -- see DoubleClicked's own doc comment.</summary>
    internal bool WantsDoubleClickDetection => DoubleClicked is not null;

    /// <summary>
    /// Raises Clicked directly, bypassing HandleClick/HandleContentClick/OnContentClickAction --
    /// used only by UiInputController's double-click-aware dispatch (DispatchClick). Safe to
    /// skip OnContentClickAction's own default recursion into children here specifically because
    /// WantsDoubleClickDetection is only ever true for a childless leaf (a grid cell)
    /// today -- revisit before adding double-click support to any compound Element.
    /// </summary>
    internal void RaiseClicked() => Clicked?.Invoke(this);

    /// <summary>See RaiseClicked.</summary>
    internal void RaiseDoubleClicked() => DoubleClicked?.Invoke(this);

    /// <summary>How many times this element has gone back to its pool, counted by ElementPoolService.CloseElement for every element a close reaches, children included.</summary>
    /// <remarks>Tells a pooled instance's later use from the one something remembered (UiInputController's double-click candidate) -- Closed can't, since it fires only for the element Close was called on.</remarks>
    internal int PoolReturnCount { get; set; }

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
    public IReadOnlyList<Element> ChildElements => _children;

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
    /// <summary>The cap this element's author set, or the screen when none was set.</summary>
    public Vector2 MaximumSize => _geometry.AuthoredMaximumSize ?? Render.ScreenSize;

    /// <summary>The space the last measure offered this element.</summary>
    public Vector2 AvailableSize => _geometry.AvailableSize;

    /// <summary>The largest this element may be now: the smaller of its MaximumSize and the space its parent last offered it.</summary>
    /// <remarks>A root is offered exactly its MaximumSize, so for a root this follows SetMaximumSize and a resized screen before any re-measure.</remarks>
    public Vector2 EffectiveMaximumSize => _parent is null ? MaximumSize : Vector2.Min(MaximumSize, _geometry.AvailableSize);

    public Rectangle Rectangle => _geometry.Rectangle;

    protected bool _isVisible = true;

    /// <summary>Whether this element is drawn, updated and given space in its parent's layout.</summary>
    /// <remarks>A change re-lays out the parent (OnChildLayoutChanged): an invisible child takes no space when tiled or wrapped around, and none of the scroll range. Shown again, the element is measured first, since a measure skips it while hidden.</remarks>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            MarkTreeChanged();

            if (_parent is null)
            {
                return;
            }

            if (value)
            {
                _hasUnreportedLayoutChange = true;
                MeasureAndArrange();
                return;
            }

            _parent.OnChildLayoutChanged();
        }
    }

    /// <summary>Set when this element was attached or moved since its parent last heard of it, so its next MeasureAndArrange reports it even if its size is unchanged.</summary>
    private bool _hasUnreportedLayoutChange;

    /// <summary>Whether the pointer is over this element or one of its descendants, as UiInputController's hit test last found it -- topmost only, so an element under another window, outside its scrolled viewport or behind menu mode is never over.</summary>
    /// <remarks>Set by UiInputController for the hovered element and every ancestor; not refreshed during a window move, resize or scrollbar drag. Cleared when the element is built from its pool.</remarks>
    public bool IsPointerOver { get; internal set; }

    private static int _treeVersion;

    /// <summary>Changes whenever any element gains or loses a child or changes visibility -- UiInputController hit-tests again when it does, so content rebuilt under a still mouse is hovered the same frame it would be after a move.</summary>
    internal static int TreeVersion => Volatile.Read(ref _treeVersion);

    private static void MarkTreeChanged() => Interlocked.Increment(ref _treeVersion);

    /// <summary>Whether this element can be found by hit-testing (FindTopmostHit/TryHitTestInteraction) at all -- defaults to just IsVisible; Button overrides this to also require Enabled, so a disabled button is never hovered, pressed, or clicked, rather than every caller having to remember to check Enabled itself.</summary>
    protected virtual bool IsHitTestable => _isVisible;

    /// <summary>
    /// Free-form consumer-defined association, the same role WPF's FrameworkElement.Tag or
    /// WinForms' Control.Tag plays -- lets a caller mark "this element belongs to/represents X"
    /// without X needing to be this element's own IElementContent (a host window whose content is
    /// driven manually, never assigned via SetContent, so Window.Content alone can't identify it).
    /// Reset to null on Build (see its own doc comment on pooled reuse) so a reused instance never
    /// inherits a stale reference from whatever it was last used for.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>What a content drop on this element lands on, for an element that isn't an IContentDropTarget itself -- a window whose content is driven manually. Reset on Build.</summary>
    public IContentDropTarget? DropTarget { get; set; }

    protected bool _isTransparent;
    public bool IsTransparent => _isTransparent;

    protected bool _isGlowing;
    public bool IsGlowing => _isGlowing;

    protected Color _glowColor = Color.Gold;
    public Color GlowColor => _glowColor;

    protected GlowMode _glowMode = GlowMode.ExteriorFade;
    public GlowMode GlowMode => _glowMode;

    protected GlowAnimation _glowAnimation;
    public GlowAnimation GlowAnimation => _glowAnimation;

    private GlowPulseClock _glowPulse;

    /// <summary>Turns the glow (see GlowRenderer) on/off around this window's border -- e.g. a folder with something unread. Drawn before the content background fill/DrawContent/children (see Draw), so an interior-facing mode here would just get painted over -- use SetOverlayGlow instead for a glow that needs to sit on top of content.</summary>
    /// <remarks>A pulse starts when the glow turns on or its animation changes; setting the same glow again leaves it running. RestartGlowPulse starts it over.</remarks>
    public void SetGlow(bool isGlowing, Color? color = null, GlowMode mode = GlowMode.ExteriorFade, GlowAnimation animation = default)
    {
        var restartsPulse = (isGlowing && !_isGlowing) || animation != _glowAnimation;
        _isGlowing = isGlowing;
        _glowColor = color ?? Color.Gold;
        _glowMode = mode;
        _glowAnimation = animation;
        if (restartsPulse)
        {
            _glowPulse.Restart();
        }
    }

    /// <summary>Starts the glow's pulse over, from the next draw -- something new arrived while it was already glowing.</summary>
    public void RestartGlowPulse() => _glowPulse.Restart();

    /// <summary>Starts the glow's pulse at presentationTime if it is waiting to start, as Draw does before drawing the glow.</summary>
    public void StartPendingGlowPulseAt(TimeSpan presentationTime) => _glowPulse.StartPendingAt(presentationTime);

    /// <summary>The glow's strength at presentationTime; full while its pulse waits to start.</summary>
    public float GlowStrengthAt(TimeSpan presentationTime) => _glowAnimation.StrengthAt(_glowPulse.ElapsedAt(presentationTime));

    protected bool _isOverlayGlowing;
    protected Color _overlayGlowColor = Color.Gold;
    protected GlowMode _overlayGlowMode = GlowMode.InteriorFade;
    protected GlowAnimation _overlayGlowAnimation;
    private GlowPulseClock _overlayGlowPulse;

    /// <summary>Turns a glow drawn over this element's content area on/off, after the content background fill and every child has drawn (see Draw) -- e.g. column panels, where a white InteriorFade vignette needs to sit on top of the dark panel fill and its rows, not get erased by them.</summary>
    /// <remarks>Its pulse starts and restarts as SetGlow's does.</remarks>
    public void SetOverlayGlow(bool isGlowing, Color? color = null, GlowMode mode = GlowMode.InteriorFade, GlowAnimation animation = default)
    {
        var restartsPulse = (isGlowing && !_isOverlayGlowing) || animation != _overlayGlowAnimation;
        _isOverlayGlowing = isGlowing;
        _overlayGlowColor = color ?? Color.Gold;
        _overlayGlowMode = mode;
        _overlayGlowAnimation = animation;
        if (restartsPulse)
        {
            _overlayGlowPulse.Restart();
        }
    }

    /// <summary>Starts the overlay glow's pulse over, from the next draw.</summary>
    public void RestartOverlayGlowPulse() => _overlayGlowPulse.Restart();

    /// <summary>Starts the overlay glow's pulse at presentationTime if it is waiting to start, as Draw does before drawing the overlay glow.</summary>
    public void StartPendingOverlayGlowPulseAt(TimeSpan presentationTime) => _overlayGlowPulse.StartPendingAt(presentationTime);

    /// <summary>The overlay glow's strength at presentationTime; full while its pulse waits to start.</summary>
    public float OverlayGlowStrengthAt(TimeSpan presentationTime) => _overlayGlowAnimation.StrengthAt(_overlayGlowPulse.ElapsedAt(presentationTime));

    /*========Focus========*/
    public event Action<Element>? FocusChanged;

    protected bool _isFocused;

    /// <summary>True while this window holds input focus -- set by UiInputController, not this window itself.</summary>
    public bool IsFocused => _isFocused;

    protected bool _isActiveWindow;

    /// <summary>
    /// True for whichever top-level (root-ancestor) element was most recently clicked into --
    /// distinct from IsFocused, which only tracks keyboard-input routing and often stays false
    /// for an entire window: HandleMousePress skips SetFocus for a button click, and clears focus
    /// to null for a click on any non-focusable content (most rows/cells/columns explicitly opt
    /// out via CanUserFocus = false), so a menu window's own IsFocused rarely reflects "the
    /// window the player is actively working in" the way this does. Set by
    /// UiInputController.RaiseToFront, which (unlike SetFocus) runs unconditionally for every
    /// click that hits anything at all -- e.g. Window uses this (not IsFocused) to decide whether
    /// to show its focus-accent border.
    /// </summary>
    public bool IsActiveWindow => _isActiveWindow;

    internal void SetIsActiveWindow(bool isActiveWindow) => _isActiveWindow = isActiveWindow;

    /*========Header========*/
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

    /*========Footer========*/
    /// <summary>
    /// Fixed-height region reserved at the bottom of this element's own (already padded) content
    /// area -- see Window.SetFooterContent, the only setter. Unlike the header, a footer has no
    /// AbsolutePosition/Rectangle/DrawFooter of its own: its content is hosted as an ordinary
    /// child Window (see Window.OnChildrenInitialized), which draws/hit-tests/scrolls-immune
    /// itself through the same pipeline every other child already uses -- there is nothing here
    /// that needs its own header-style escape hatch. Only meaningful for Fixed/Fill display
    /// modes -- RecalculateWrapContentSize's own content-size-from-children scan already includes
    /// the footer host window as an ordinary child, so adding FooterHeight on top the way header
    /// does would double-count it; no current consumer is WrapContent, so this is left
    /// unsupported rather than solved speculatively. Also only correct while this element itself
    /// is not scrollable (CanUserScrollVertical/Horizontal false) -- Draw's children clip
    /// intersects with ContentRectangle, which by construction ends exactly where the footer
    /// begins, so a scrollable element's own footer would be clipped away entirely; neither
    /// current consumer's outer window scrolls itself, only an inner content window does.
    /// </summary>
    public float FooterHeight { get; private protected set; }

    public bool ShowFooter => FooterHeight > 0f;

    /// <summary>Draws this element's header region -- Window's title bar (background/text/buttons) and Folder's icon are both just their own override of this, called from Draw whenever the header is shown (see the ShowHeader/ShowHeaderWhenMinimized gate there). No-op by default. Overrides needing the batch or Texture2D read them from Render rather than taking them as parameters.</summary>
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
    /// Border thickness on one edge if the border is shown, else zero -- the one source
    /// of truth for how much border eats into title/content space.
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

    /// <summary>
    /// ContentPadding if this element is capable of containing children at all, else zero -- a
    /// leaf element (CanContainChildren false) draws its own self-drawn content flush against its
    /// content edges, unaffected by ContentPadding (see its own doc comment). Gated on
    /// CanContainChildren alone, not "and currently has at least one child" -- a container is
    /// structurally committed to eventually holding padded children the moment it's built that
    /// way, and reading ContentSize/ContentAbsolutePosition while transiently childless (e.g. the
    /// gap between ClearAllChildren and repopulating) must already reflect that, or the first
    /// child added back gets sized/positioned against a stale, wider box than the one it actually
    /// ends up in once its own presence flips the gate.
    /// </summary>
    protected Vector2 ChildContentPadding =>
        _canContainChildren
            ? ContentPadding
            : Vector2.Zero;

    /// <summary>ChildContentPadding on both edges of each axis -- see its own doc comment.</summary>
    protected Vector2 ChildContentPaddingDoubled => ChildContentPadding * 2;

    public Rectangle BorderTopRectangle => _border.TopRectangle;
    public Rectangle BorderBottomRectangle => _border.BottomRectangle;
    public Rectangle BorderLeftRectangle => _border.LeftRectangle;
    public Rectangle BorderRightRectangle => _border.RightRectangle;
    public BorderStyle BorderStyle { get => _border.Style; set => _border.Style = value; }
    public Color BorderColor => _border.Color;

    /*========Content========*/
    /// <summary>Content-area bookkeeping (ElementContentState). Named _contentState, not _content, to avoid colliding with the name of Window's own pluggable IElementContent field.</summary>
    private protected readonly ElementContentState _contentState = new();

    public Vector2 ContentAbsolutePosition => _contentState.AbsolutePosition;
    public Vector2 ContentSize => _contentState.Size;
    public Rectangle ContentRectangle => _contentState.Rectangle;
    /// <summary>
    /// Uniform inset between this element's own edges and its children's content area (see
    /// RecalculateAbsolutePositions/RecalculateFillSize) -- also independently used by TextBox/
    /// TextWindow for their own text-wrap-width math, which is unaffected by the has-children
    /// gate below since neither has children. Defaults to the generic Chrome.WindowChrome.Padding
    /// value, not a literal, so a future data-driven theme loader can retune it globally.
    /// </summary>
    public Vector2 ContentPadding { get; set; } = new(WindowChrome.Padding);
    public Color ContentColor => _contentState.BackgroundColor;

    /// <summary>Changes the content background color after creation -- ElementContentOptions.ContentColor only ever sets it once, at CreateElement time; this is for a control that needs its own background to react to later state changes (e.g. GridControl's toggle buttons tinting differently once on).</summary>
    public void SetContentColor(Color color) => _contentState.BackgroundColor = color;

    /// <summary>The colour the content background is filled with: ContentColor, unless a control's state changes how it looks (a disabled text box darkens it).</summary>
    protected virtual Color ContentBackgroundColor => _contentState.BackgroundColor;

    /// <summary>Changes the gap tiled after this element (see RetileChildren) after creation -- ElementLayoutOptions.SpacingAfter only ever sets it once, at CreateElement time; this is for a pooled/reused element whose spacing needs to change later.</summary>
    public void SetSpacingAfter(float spacingAfter) => _geometry.SpacingAfter = spacingAfter;

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

    /// <summary>Shown in the shared tooltip once the pointer has rested on this element for PopupChrome.HoverTooltipDelayFrames (UiInputController); null shows nothing.</summary>
    public string? TooltipText { get; set; }

    protected virtual bool RequiresContentViewport => CanUserScrollHorizontal || CanUserScrollVertical;

    /*========Scroll========*/
    private Vector2 _scrollOffset;
    public Vector2 ScrollOffset => _scrollOffset;
    protected Vector2 _maxScrollOffset;
    public Vector2 MaxScrollOffset => _maxScrollOffset;

    /*========Scrollbars========*/
    private protected readonly ElementScrollbarState _scrollbars = new();

    public ScrollbarVisibility ScrollbarVisibility { get => _scrollbars.Visibility; set => _scrollbars.Visibility = value; }
    public bool ShowVerticalScrollbar => _scrollbars.ShowVertical;
    public bool ShowHorizontalScrollbar => _scrollbars.ShowHorizontal;
    public Rectangle VerticalScrollbarTrackRectangle => _scrollbars.VerticalTrackRectangle;
    public Rectangle VerticalScrollbarThumbRectangle => _scrollbars.VerticalThumbRectangle;
    public Rectangle HorizontalScrollbarTrackRectangle => _scrollbars.HorizontalTrackRectangle;
    public Rectangle HorizontalScrollbarThumbRectangle => _scrollbars.HorizontalThumbRectangle;

    /// <summary>Space the shown scrollbars take out of the content area: X for a vertical bar, Y for a horizontal one.</summary>
    protected Vector2 ScrollbarGutterSize => new(
        _scrollbars.ShowVertical ? WindowChrome.ScrollbarThickness : 0f,
        _scrollbars.ShowHorizontal ? WindowChrome.ScrollbarThickness : 0f);

    /// <summary>How much larger this element is than its content area on each axis -- border, header, padding, footer and the scrollbars shown -- whatever its current size, so a caller can size it to fit content it knows the size of.</summary>
    public Vector2 ContentInset => BorderInsetDoubled + ChildContentPaddingDoubled + new Vector2(0, HeaderInsetHeight + FooterHeight) + ScrollbarGutterSize;

    /// <summary>Overflow below this many pixels doesn't earn a scrollbar.</summary>
    private const float ScrollbarOverflowThreshold = 0.5f;

    /// <summary>Re-measures Measure may run after the first to settle which bars show -- one per axis.</summary>
    private const int MaximumScrollbarSettlePasses = 2;

    /// <summary>How many of this element's own Measure calls are in progress; a scrollbar change found by a child event while measuring is left to Measure's own settle loop.</summary>
    private int _measureDepth;

    public virtual void Build(Element? parent, ElementOptions options)
    {
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
        DropTarget = null;
        TooltipText = null;

        /*========Display========*/
        _geometry.DisplayMode = layout?.DisplayMode ?? ElementDisplayMode.Fixed;
        _geometry.RelativePosition = layout?.RelativePosition ?? new Vector2();
        _geometry.AbsolutePosition = _parent != null
            ? _parent.ContentAbsolutePosition + _geometry.RelativePosition
            : _geometry.RelativePosition;

        _geometry.MinimumSize = layout?.MinimumSize ?? new Vector2(0, 0);
        _geometry.AuthoredMaximumSize = layout?.MaximumSize;
        _geometry.AvailableSize = MaximumSize;
        _geometry.OriginalSize = layout?.Size ?? new Vector2(0, 0);
        _geometry.CurrentSize = _geometry.OriginalSize;
        _geometry.SpacingAfter = layout?.SpacingAfter ?? 0f;

        _isVisible = layout?.IsVisible ?? true;
        IsPointerOver = false;
        _isTransparent = layout?.IsTransparent ?? false;

        // Pooled windows must not inherit a stale glow from whatever they were last used for --
        // same rationale as _isFocused reset below.
        _isGlowing = false;
        _glowColor = Color.Gold;
        _glowMode = GlowMode.ExteriorFade;
        _glowAnimation = GlowAnimation.Static;
        _glowPulse.Reset();
        _isOverlayGlowing = false;
        _overlayGlowColor = Color.Gold;
        _overlayGlowMode = GlowMode.InteriorFade;
        _overlayGlowAnimation = GlowAnimation.Static;
        _overlayGlowPulse.Reset();

        /*========Focus========*/
        // Pooled windows (see WindowService) must not inherit a stale focused look from
        // whatever they were last used for.
        _isFocused = false;
        _isActiveWindow = false;

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
        _contentState.BackgroundColor = content?.ContentColor ?? WindowPalette.PanelBackgroundColor;

        /*========User Controls========*/
        CanUserMove = chrome?.CanUserMove ?? false;
        CanUserResize = chrome?.CanUserResize ?? false;
        CanUserScrollHorizontal = chrome?.CanUserScrollHorizontal ?? false;
        CanUserScrollVertical = chrome?.CanUserScrollVertical ?? false;
        CanUserFocus = chrome?.CanUserFocus ?? true;

        /*========Scroll========*/
        _scrollOffset = Vector2.Zero;
        _maxScrollOffset = Vector2.Zero;

        /*========Scrollbars========*/
        _scrollbars.Visibility = chrome?.ScrollbarVisibility ?? ScrollbarVisibility.Auto;
        _scrollbars.ShowVertical = false;
        _scrollbars.ShowHorizontal = false;
        _scrollbars.HoveredPart = ScrollbarPart.None;
        _scrollbars.PressedPart = ScrollbarPart.None;
    }

    public virtual void Initialize()
    {
        // Under a Minimized parent this measures nothing (see MeasureAndArrange): the element keeps
        // its Build-time CurrentSize/RelativePosition until the parent's next real (non-Minimized)
        // Measure/Arrange pass reaches it. Under a WrapContent parent it also re-fits the parent.
        MeasureAndArrange();

        RecalculateCameraTransform();

        InitializeHeaderExtras();

        foreach (var childElement in _children)
        {
            childElement.Initialize();
        }

        // Runs after the loop above (not before): content that adds its own child windows
        // does so via AddChild, which already initializes each
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
    /// coordinates it always has; RequiresContentViewport's draw offset (see Draw) picks the
    /// scroll up automatically.
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
        // drawn at the content's offset at all -- see Draw's child-window loop, which keeps the
        // inherited draw offset and only clips -- so
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
    /// -ScrollOffset as a translation: content drawn in local coordinates (RequiresContentViewport's
    /// scope, see Draw) shifts by this as well as by the viewport's origin, which is what makes scrolling
    /// work without any DrawContent implementation needing to know about ScrollOffset itself.
    /// No rotation or zoom.
    /// </summary>
    private void RecalculateCameraTransform()
    {
        _cameraTransform = Matrix.CreateTranslation(-_scrollOffset.X, -_scrollOffset.Y, 0);
    }

    public virtual void Update(GameTime gameTime)
    {
        UpdateHeaderExtras(gameTime);

        // A Minimized element's children are neither drawn nor hit-tested (see Draw and
        // TryHitTestInteraction), so they don't tick either until it is restored.
        if (_geometry.DisplayMode == ElementDisplayMode.Minimized)
        {
            return;
        }

        foreach (var childElement in _children)
        {
            // Gated here, at the caller, rather than inside Update() itself -- subclasses
            // (Window among them) call base.Update() and then do
            // their own additional work afterward, which an early-return guard at the top of
            // this base method couldn't prevent. Skipping the call entirely, before it's ever
            // made, is airtight regardless of what an override's own Update body does.
            //
            // Root elements are deliberately NOT covered by this -- see
            // UiLayerHost.Update, which still calls Update on every root window
            // regardless of IsVisible. A persistent Tooltip is toggled
            // via IsVisible while staying in UiLayer.Tooltip's root list and is driven externally,
            // so its Update is a no-op either way. The exclusion is the conservative default: a genuine parent/child relationship (this loop)
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

    /// <summary>Reads the batch and UnitRectangle from Render rather than taking them as parameters -- every override, and every recursive child Draw call below, does the same.</summary>
    public virtual void Draw(GameTime gameTime)
    {
        if (!_isVisible)
        {
            return;
        }

        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (_border.Show)
        {
            BorderRenderer.Draw(spriteBatch, unitRectangle, _border.Style, _border.Color, _border.TopRectangle, _border.BottomRectangle, _border.LeftRectangle, _border.RightRectangle);
        }

        if (_isGlowing)
        {
            StartPendingGlowPulseAt(gameTime.TotalGameTime);
            GlowRenderer.Draw(spriteBatch, unitRectangle, _geometry.Rectangle, _glowColor, _glowMode, GlowStrengthAt(gameTime.TotalGameTime));
        }

        if ((_geometry.DisplayMode != ElementDisplayMode.Minimized && _headerState.ShowHeader) || (_geometry.DisplayMode == ElementDisplayMode.Minimized && _headerState.ShowHeaderWhenMinimized))
        {
            DrawHeader(gameTime);
        }

        if (_geometry.DisplayMode != ElementDisplayMode.Minimized)
        {
            if (!_isTransparent)
            {
                // BackgroundRectangle, not Rectangle -- this element's own fill covers its full
                // content area edge-to-edge; ContentPadding only insets where CHILDREN sit within
                // that area (see ElementContentState's own doc comment), so filling just Rectangle
                // would leave the padding band itself unpainted, a visible gap of whatever's behind
                // this element between its border and its (deliberately smaller) content fill.
                spriteBatch.Draw(unitRectangle, _contentState.BackgroundRectangle, ContentBackgroundColor);
            }

            // The content is drawn in local coordinates, offset to ContentDrawOrigin, and a scrollable element's is
            // clipped to its viewport within whatever clip is already in force. Pushed/popped through Render, so an
            // Element nested inside another hands back the exact state it inherited.
            var contentRenderState = RequiresContentViewport ? Render.CurrentRenderState.ClippedTo(Viewport.Bounds) : Render.CurrentRenderState;
            Render.PushRenderState(contentRenderState with { DrawOffset = ContentDrawOrigin });
            DrawContent(gameTime);
            Render.PopRenderState();

            if (RequiresContentViewport && _children.Count > 0)
            {
                // Unlike the content above, children draw in absolute screen coordinates (ScrollBy folds the scroll into
                // their positions), so they keep the inherited offset and only need clipping -- a child scrolled out of
                // view would otherwise draw in full wherever that puts it. Intersected with whatever clip is already in
                // force, since scrollable windows nest (TabbedContent's scrollable body hosting a scrollable grid).
                Render.PushRenderState(Render.CurrentRenderState.ClippedTo(_contentState.Rectangle));

                foreach (var childElement in _children)
                {
                    childElement.Draw(gameTime);
                }

                Render.PopRenderState();
            }
            else
            {
                foreach (var childElement in _children)
                {
                    childElement.Draw(gameTime);
                }
            }

            if (_isOverlayGlowing)
            {
                StartPendingOverlayGlowPulseAt(gameTime.TotalGameTime);
                GlowRenderer.Draw(spriteBatch, unitRectangle, _contentState.Rectangle, _overlayGlowColor, _overlayGlowMode, OverlayGlowStrengthAt(gameTime.TotalGameTime));
            }

            if (_scrollbars.ShowVertical || _scrollbars.ShowHorizontal)
            {
                DrawScrollbars(spriteBatch, unitRectangle);
            }
        }
    }

    private void DrawScrollbars(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_scrollbars.ShowVertical)
        {
            spriteBatch.Draw(unitRectangle, _scrollbars.VerticalTrackRectangle, WindowPalette.ScrollbarTrackColor);
            spriteBatch.Draw(unitRectangle, _scrollbars.VerticalThumbRectangle, GetScrollbarThumbColor(ScrollbarPart.VerticalThumb));
        }

        if (_scrollbars.ShowHorizontal)
        {
            spriteBatch.Draw(unitRectangle, _scrollbars.HorizontalTrackRectangle, WindowPalette.ScrollbarTrackColor);
            spriteBatch.Draw(unitRectangle, _scrollbars.HorizontalThumbRectangle, GetScrollbarThumbColor(ScrollbarPart.HorizontalThumb));
        }

        if (_scrollbars.ShowVertical && _scrollbars.ShowHorizontal)
        {
            spriteBatch.Draw(unitRectangle, _scrollbars.CornerRectangle, WindowPalette.ScrollbarTrackColor);
        }
    }

    private Color GetScrollbarThumbColor(ScrollbarPart thumb) =>
        _scrollbars.PressedPart == thumb
            ? WindowPalette.ScrollbarThumbPressedColor
            : _scrollbars.HoveredPart == thumb
                ? WindowPalette.ScrollbarThumbHoverColor
                : WindowPalette.ScrollbarThumbColor;

    internal void SetScrollbarHoveredPart(ScrollbarPart part) => _scrollbars.HoveredPart = part;

    internal void SetScrollbarPressedPart(ScrollbarPart part) => _scrollbars.PressedPart = part;

    internal ScrollbarPart ScrollbarHoveredPart => _scrollbars.HoveredPart;

    internal ScrollbarPart ScrollbarPressedPart => _scrollbars.PressedPart;

    /// <summary>Which of this element's own shown scrollbars (not its children's) is under position.</summary>
    internal ScrollbarPart GetScrollbarPartAt(Point position)
    {
        if (_geometry.DisplayMode == ElementDisplayMode.Minimized)
        {
            return ScrollbarPart.None;
        }

        if (_scrollbars.ShowVertical)
        {
            if (_scrollbars.VerticalThumbRectangle.Contains(position))
            {
                return ScrollbarPart.VerticalThumb;
            }
            if (_scrollbars.VerticalTrackRectangle.Contains(position))
            {
                return ScrollbarPart.VerticalTrack;
            }
        }

        if (_scrollbars.ShowHorizontal)
        {
            if (_scrollbars.HorizontalThumbRectangle.Contains(position))
            {
                return ScrollbarPart.HorizontalThumb;
            }
            if (_scrollbars.HorizontalTrackRectangle.Contains(position))
            {
                return ScrollbarPart.HorizontalTrack;
            }
        }

        return ScrollbarPart.None;
    }

    /// <summary>The topmost scrollbar anywhere in this element's subtree under position, or NotHit.</summary>
    /// <remarks>An element's own bars are checked before its children's, the same order TryHitTestInteraction uses: they sit in its gutter, outside the content area its children are clipped to.</remarks>
    internal ElementInteraction TryHitTestScrollbar(Point position)
    {
        if (!IsHitTestable || !_geometry.Rectangle.Contains(position))
        {
            return ElementInteraction.NotHit;
        }

        var ownPart = GetScrollbarPartAt(position);
        if (ownPart != ScrollbarPart.None)
        {
            return ElementInteraction.Scrollbar(this, ownPart);
        }

        var childCount = _geometry.DisplayMode == ElementDisplayMode.Minimized ? 0 : _children.Count;
        for (var index = childCount - 1; index >= 0; index--)
        {
            var childInteraction = _children[index].TryHitTestScrollbar(position);
            if (childInteraction.Element is not null)
            {
                return childInteraction;
            }
        }

        return ElementInteraction.NotHit;
    }

    /// <summary>Scrolls to an absolute offset, clamped the same way ScrollBy clamps.</summary>
    public void ScrollTo(Vector2 scrollOffset) => ScrollBy(scrollOffset - _scrollOffset);

    /// <summary>Scrolls so the thumb sits where it started a drag, moved by the mouse's total movement along its axis since then.</summary>
    /// <remarks>Measured from the offset at the start of the drag rather than accumulated per frame, the same anchor a right-drag pan uses, so clamping at either end never loses ground.</remarks>
    internal void DragScrollbarThumb(ScrollbarPart thumb, Vector2 dragStartScrollOffset, Vector2 totalMouseDelta)
    {
        if (thumb.IsVertical())
        {
            var thumbTravel = _scrollbars.VerticalTrackRectangle.Height - _scrollbars.VerticalThumbRectangle.Height;
            if (thumbTravel > 0)
            {
                ScrollTo(new Vector2(_scrollOffset.X, dragStartScrollOffset.Y + totalMouseDelta.Y * _maxScrollOffset.Y / thumbTravel));
            }
        }
        else
        {
            var thumbTravel = _scrollbars.HorizontalTrackRectangle.Width - _scrollbars.HorizontalThumbRectangle.Width;
            if (thumbTravel > 0)
            {
                ScrollTo(new Vector2(dragStartScrollOffset.X + totalMouseDelta.X * _maxScrollOffset.X / thumbTravel, _scrollOffset.Y));
            }
        }
    }

    /// <summary>-1 if position is before the track's thumb along its axis, 1 if after, 0 if level with it.</summary>
    internal int GetScrollbarPageDirectionToward(ScrollbarPart track, Point position)
    {
        if (track.IsVertical())
        {
            var thumb = _scrollbars.VerticalThumbRectangle;
            return position.Y < thumb.Top ? -1 : position.Y >= thumb.Bottom ? 1 : 0;
        }

        var horizontalThumb = _scrollbars.HorizontalThumbRectangle;
        return position.X < horizontalThumb.Left ? -1 : position.X >= horizontalThumb.Right ? 1 : 0;
    }

    /// <summary>Scrolls one visible length along the track's axis, backward for a negative direction and forward for a positive one.</summary>
    internal void PageScrollbar(ScrollbarPart track, int direction)
    {
        var pageDelta = track.IsVertical()
            ? new Vector2(0, _contentState.Size.Y)
            : new Vector2(_contentState.Size.X, 0);
        ScrollBy(pageDelta * System.Math.Sign(direction));
    }


    /// <summary>Draws this element's own content, in local coordinates: (0, 0) is the content area's top-left, scrolled with it -- see ContentDrawOrigin.</summary>
    /// <remarks>
    /// No-op by default; TextWindow and other self-drawn elements override this directly, Window overrides it to host
    /// IElementContent. Overrides needing the batch or Texture2D read them from Render rather than taking them as
    /// parameters. Something placed in screen coordinates -- the cursor, another element's rectangle -- is converted with
    /// ToContentLocal; LocalContentRectangle is the content area itself.
    /// </remarks>
    public virtual void DrawContent(GameTime gameTime) { }

    /// <summary>Where DrawContent's (0, 0) is on screen: the content area's top-left, less the scroll for a scrollable element.</summary>
    public Vector2 ContentDrawOrigin => RequiresContentViewport
        ? new Vector2(Viewport.X + CameraTransform.Translation.X, Viewport.Y + CameraTransform.Translation.Y)
        : ContentAbsolutePosition;

    /// <summary>The content area in DrawContent's local coordinates: at (0, 0), ContentRectangle's size.</summary>
    public Rectangle LocalContentRectangle => new(0, 0, _contentState.Rectangle.Width, _contentState.Rectangle.Height);

    /// <summary>screenPosition in DrawContent's local coordinates.</summary>
    public Vector2 ToContentLocal(Vector2 screenPosition) => screenPosition - ContentDrawOrigin;

    /// <summary>screenRectangle in DrawContent's local coordinates.</summary>
    public Rectangle ToContentLocal(Rectangle screenRectangle)
    {
        var origin = ContentDrawOrigin;
        return new Rectangle(screenRectangle.X - (int)origin.X, screenRectangle.Y - (int)origin.Y, screenRectangle.Width, screenRectangle.Height);
    }

    /// <summary>Routes a key newly pressed this frame to this window while it holds focus -- see UiInputController.RouteKeyPressesToFocusedWindow.</summary>
    internal void HandleKeyPress(Keys key) => OnKeyPressAction(key);

    protected virtual void OnKeyPressAction(Keys key) { }

    /// <summary>
    /// Routes the whole keyboard state to this window once per frame while it holds focus --
    /// see UiInputController.RouteHotkeysToFocusedWindow. Unlike HandleKeyPress (one discrete
    /// key-press event at a time), this is for windows whose own hotkeys need continuous or
    /// combined multi-key state (scrolling by WASD reads all four keys'
    /// down-state together rather than reacting to one press event).
    /// </summary>
    internal void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => OnHotkeysAction(keyboardState, previousKeyboardState);

    protected virtual void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState) { }

    /// <summary>Shared "newly pressed this frame" edge-detection for OnHotkeysAction overrides and UiInputController's own Tab handling.</summary>
    public static bool WasKeyPressed(KeyboardState current, KeyboardState previous, Keys key) => current.IsKeyDown(key) && previous.IsKeyUp(key);

    /// <summary>
    /// Fires once when a right-mouse-button drag starts over this window -- see
    /// UiInputController's right-button state machine. No-op by default; a panning view
    /// snapshots its scroll position here as the drag's anchor, so every subsequent
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
    /// HandleRightDragStart. No-op by default (a map view pans its camera
    /// here); unlike HandleHotkeys this doesn't depend on focus, since a drag-to-pan gesture
    /// shouldn't require clicking to focus a window first.
    /// </summary>
    internal void HandleRightDrag(Vector2 totalPixelDeltaSinceStart) => OnRightDragAction(totalPixelDeltaSinceStart);

    protected virtual void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) { }

    /// <summary>
    /// Fires once when an in-progress right-mouse-button drag ends (button released) -- see
    /// UiInputController's right-button state machine. No-op by default; a panning view can
    /// settle its scroll onto a grid here once the gesture is over, rather than mid-drag.
    /// </summary>
    internal void HandleRightDragEnd() => OnRightDragEndAction();

    protected virtual void OnRightDragEndAction() { }

    /// <summary>
    /// Fires instead of HandleRightDragEnd when a right-button press/release never moved the
    /// mouse past UiInputController's small tap-vs-drag pixel threshold -- a right-click
    /// "tap," distinct from the drag-to-pan gesture the same button also drives. No-op by
    /// default; TextBox opens its Cut/Copy/Paste/Select All menu here. Returns whether this element
    /// handled it -- UiInputController offers the tap to the element under the cursor, then to each
    /// ancestor until one does.
    /// </summary>
    internal bool HandleRightClickTap(Point position) => OnRightClickTapAction(position);

    /// <summary>
    /// Settable per-instance right-click handler -- lets any Element opt into a right-click
    /// context menu without its own OnRightClickTapAction override -- the owning controller, not
    /// this Element, usually knows what the menu should contain (DynamicHudContextMenus' Close/Close All
    /// for a window, say). The base OnRightClickTapAction below just
    /// invokes this; a subclass with genuinely custom right-click logic (TextBox)
    /// still overrides OnRightClickTapAction directly instead and never touches this field.
    /// </summary>
    public Action<Point>? OnRightClicked { get; set; }

    /// <summary>Invokes OnRightClicked; handled exactly when there is one. An override that does something returns true.</summary>
    protected virtual bool OnRightClickTapAction(Point position)
    {
        if (OnRightClicked is not { } onRightClicked)
        {
            return false;
        }

        onRightClicked(position);
        return true;
    }

    /// <summary>
    /// Fires on every root/HUD window when Escape is pressed -- see
    /// UiInputController.HandleEscape for why this is broadcast unconditionally rather than
    /// routed only to whichever window holds focus. No-op by default; a view can cancel
    /// whatever the player has armed here.
    /// </summary>
    internal void HandleEscape() => OnEscapeAction();

    protected virtual void OnEscapeAction() { }

    /// <summary>Offers the mouse wheel to this element before any scrollable ancestor scrolls -- notches is positive away from the user. Returns whether it was used.</summary>
    internal bool HandleMouseWheel(int notches) => OnMouseWheelAction(notches);

    /// <summary>Uses nothing by default, so the wheel scrolls; a number field steps its value.</summary>
    protected virtual bool OnMouseWheelAction(int notches) => false;

    /// <summary>Whether a press at position drags something of this element's own -- a split pane's splitter -- ahead of its children.</summary>
    protected virtual bool IsPointerDragHandleAt(Point position) => false;

    /// <summary>The cursor over and while dragging what IsPointerDragHandleAt reports.</summary>
    protected internal virtual MouseCursor PointerDragCursor => MouseCursor.Arrow;

    /// <summary>Called every frame of a pointer drag with the mouse's total movement since the press.</summary>
    internal void DragPointer(Vector2 totalMouseDelta) => OnPointerDragAction(totalMouseDelta);

    protected virtual void OnPointerDragAction(Vector2 totalMouseDelta) { }

    /// <summary>Called once when the press that started a pointer drag is released.</summary>
    internal void EndPointerDrag() => OnPointerDragEndedAction();

    protected virtual void OnPointerDragEndedAction() { }

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
    /// tile, button and non-editable window sets CanUserFocus = false), so any future focusable widget (e.g. a dropdown or
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

    /// <summary>A click at mousePosition on this element itself -- its header, or its content; returns whether this element handled it.</summary>
    /// <remarks>
    /// The header always handles its own clicks. A content click is handled when OnContentClickAction says so or anything
    /// listens to Clicked; otherwise UiInputController offers it to the parent, so a label inside a clickable row clicks
    /// the row. A click outside both is never handled.
    /// </remarks>
    public bool HandleClick(Point mousePosition)
    {
        // _header.Rectangle is sized/positioned from _header.Size/_header.AbsolutePosition
        // unconditionally (RecalculateRectangles doesn't know about _header.ShowHeader), so
        // without this guard a header-less element's upper region is a dead zone: clicks there
        // land in an invisible header rect and route to HandleHeaderClick (which only checks
        // header buttons, of which there are none) instead of falling through to content.
        if (_headerState.ShowHeader && _headerState.Rectangle.Contains(mousePosition))
        {
            HandleHeaderClick(mousePosition);
            return true;
        }

        // BackgroundRectangle, not Rectangle -- a click inside the padding band (visually part
        // of this element's own painted content, see BackgroundRectangle's own doc comment)
        // must still count as a content click, not fall through to a dead zone just because it
        // missed the smaller, children-only Rectangle.
        return _contentState.BackgroundRectangle.Contains(mousePosition) && HandleContentClick(mousePosition);
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
    /// position, or null -- the title-button lookup shared by Window's FindHeaderButtonAt (the
    /// hit test) and OnHeaderClickAction (the click), so the two never disagree.
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

    private static bool IsResizeCorner(ResizeEdges edges) =>
        (edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0 && (edges & (ResizeEdges.Left | ResizeEdges.Right)) != 0;

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
                // A scrollbar near this element's edge (its own, or a child column's) sits inside
                // the resize grab band and would be ungrabbable if resize always won -- the bar is
                // the thing actually drawn there. Corners stay resize: two-axis resize lives there.
                if (!IsResizeCorner(edges) && TryHitTestScrollbar(position) is { Element: not null } scrollbarInteraction)
                {
                    return scrollbarInteraction;
                }

                return ElementInteraction.Resize(this, edges);
            }
        }

        var ownScrollbarPart = GetScrollbarPartAt(position);
        if (ownScrollbarPart != ScrollbarPart.None)
        {
            return ElementInteraction.Scrollbar(this, ownScrollbarPart);
        }

        if (IsPointerDragHandleAt(position))
        {
            return ElementInteraction.PointerDrag(this);
        }

        // A Minimized element's children aren't drawn, so they are never what was hit, wherever they sit.
        var childCount = _geometry.DisplayMode == ElementDisplayMode.Minimized ? 0 : _children.Count;
        for (var index = childCount - 1; index >= 0; index--)
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
    /// it belongs to, since Window itself has no knowledge of those. Also a no-op inside a
    /// Horizontal or Vertical parent: tiled children never overlap, so z-order means nothing
    /// there, while the child list's order IS the tiling order -- raising a pressed row moved it
    /// to the end of its list the next time the list was arranged.
    /// </summary>
    internal void RaiseToFront()
    {
        if (_parent is null || _parent._childrenTileMode != ChildElementTileMode.Floating)
        {
            return;
        }

        var siblings = _parent._children;
        siblings.Remove(this);
        siblings.Add(this);
    }

    private bool HandleContentClick(Point mousePosition)
    {
        var handled = OnContentClickAction(mousePosition);
        if (Clicked is not { } clicked)
        {
            return handled;
        }

        clicked(this);
        return true;
    }

    /// <summary>What a content click on this element itself does; returns whether it did anything. Nothing by default -- a click on a child goes to the child first, never down from here.</summary>
    protected virtual bool OnContentClickAction(Point mousePosition) => false;

    /// <summary>
    /// Attaches newChild to this element and initializes it -- callers must NOT also call
    /// newChild.Initialize() themselves afterward: a second Initialize of a Window that adds its own
    /// children creates a second, orphaned set of them. Positioning is handled separately, by Arrange (see RetileChildren)
    /// rather than here, so it's correct regardless of when in this element's lifecycle newChild
    /// was added -- see RetileChildren's own doc comment for why that split matters.
    /// </summary>
    public void AddChild(Element newChild, int? insertIndex = null)
    {
        if (!_canContainChildren)
        {
            return;
        }

        var maximumIndex = _children.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _children.Insert(clampedInsertIndex, newChild);
        MarkTreeChanged();

        // Measures newChild, which then reports itself to this element as a layout change
        // (OnChildLayoutChanged): re-fit, re-tile, scroll range.
        newChild._hasUnreportedLayoutChange = true;
        newChild.Initialize();
    }

    /// <summary>Removes the child, then retiles the remaining ones so later siblings close the gap instead of keeping the removed window's slot as dead space.</summary>
    public void RemoveChild(Guid elementId)
    {
        var childElementIndex = _children.FindIndex(childElement => childElement.ElementId == elementId);
        if (childElementIndex < 0)
        {
            return;
        }

        _children.RemoveAt(childElementIndex);
        MarkTreeChanged();

        // Later siblings close the gap, a WrapContent element shrinks around what is left, and
        // the scroll range shrinks with it.
        OnChildLayoutChanged();
    }

    /// <summary>Depth of nested BeginLayoutBatch scopes currently open on this element -- 0 means none.</summary>
    private int _layoutBatchDepth;

    /// <summary>Set by AddChild/RemoveChild when a WrapContent re-fit or a scrollbar change was suppressed because a layout batch was open -- tells the outermost scope's Dispose to actually perform the deferred MeasureAndArrange once, rather than unconditionally running one even when nothing was actually added/removed inside the batch.</summary>
    private bool _layoutBatchNeedsMeasureAndArrange;

    private void MeasureAndArrangeNowOrDeferToLayoutBatch()
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
    /// generalized to any number of AddChild/RemoveChild calls (building one row per entry, say). Reentrant:
    /// nested batches on the same element collapse into the outermost one, so a helper method
    /// that opens its own batch still composes correctly when called from within a caller's
    /// batch. Opt-in rather than automatic -- every AddChild/RemoveChild call outside a batch
    /// behaves exactly as before, so nothing that currently reads geometry synchronously right
    /// after a mutation is affected unless it explicitly opts into batching.
    ///
    /// Does not defer re-tiling (see OnChildLayoutChanged and RetileChildren) -- that's cheap
    /// (no remeasuring) and keeping it immediate means a child's own position is always correct
    /// immediately after AddChild returns, batch or not; only the WrapContent size refit (the
    /// actually expensive full remeasure) is deferred.
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

    /// <summary>Re-measures this element if its MaxScrollOffset changed which scrollbars Auto calls for, outside of its own Measure.</summary>
    /// <remarks>Inside Measure the settle loop handles it instead; inside a layout batch it's deferred to the batch's end, so clearing and refilling a grid re-measures once rather than flipping the bar on every child.</remarks>
    protected void SettleScrollbarsOutsideMeasure()
    {
        if (_measureDepth > 0 || _geometry.DisplayMode == ElementDisplayMode.Minimized || !AutoScrollbarVisibilityDiffers(out _, out _))
        {
            return;
        }

        MeasureAndArrangeNowOrDeferToLayoutBatch();
    }

    private bool AutoScrollbarVisibilityDiffers(out bool showVertical, out bool showHorizontal)
    {
        var isAuto = _scrollbars.Visibility == ScrollbarVisibility.Auto;
        showVertical = isAuto && CanUserScrollVertical && _maxScrollOffset.Y > ScrollbarOverflowThreshold;
        showHorizontal = isAuto && CanUserScrollHorizontal && _maxScrollOffset.X > ScrollbarOverflowThreshold;
        return showVertical != _scrollbars.ShowVertical || showHorizontal != _scrollbars.ShowHorizontal;
    }

    /// <summary>A scrollable element's MaxScrollOffset: how far its children's extent exceeds its content size.</summary>
    private void RecalculateScrollBoundsFromChildren()
    {
        var childrenExtent = ChildrenExtent();
        SetMaxScrollOffset(new Vector2(childrenExtent.X - _contentState.Size.X, childrenExtent.Y - _contentState.Size.Y));
    }

    /// <summary>The right and bottom edges of this element's visible children, in content coordinates: what a WrapContent element sizes itself around and a scrolling one scrolls over.</summary>
    /// <remarks>
    /// Tiled children are placed where RetileChildren will put them from their current sizes, not where the last
    /// Arrange left them: Measure runs before Arrange, so a child whose size just changed hasn't been re-tiled yet.
    /// Relative position + measured size, never the absolute Rectangle, for the same reason.
    /// </remarks>
    private Vector2 ChildrenExtent()
    {
        var extent = Vector2.Zero;
        var nextTileOffset = 0f;
        foreach (var childElement in _children)
        {
            if (!childElement.IsVisible)
            {
                continue;
            }

            var childSize = childElement._geometry.CurrentSize;
            var childPosition = _childrenTileMode switch
            {
                ChildElementTileMode.Horizontal => new Vector2(nextTileOffset, 0),
                ChildElementTileMode.Vertical => new Vector2(0, nextTileOffset),
                _ => childElement._geometry.RelativePosition,
            };
            nextTileOffset += (_childrenTileMode == ChildElementTileMode.Horizontal ? childSize.X : childSize.Y) + childElement._geometry.SpacingAfter;
            extent = Vector2.Max(extent, childPosition + childSize);
        }

        return extent;
    }

    /// <summary>Re-lays out this element after one of its children was added, removed, shown, hidden, resized or moved outside this element's own Measure.</summary>
    /// <remarks>
    /// The one place a parent reacts to a child: a WrapContent element re-fits around its children (and so on up its
    /// WrapContent ancestors), deferred to the end of an open layout batch, though tiled children are re-tiled at once
    /// so their positions are right straight after AddChild (see BeginLayoutBatch); any other re-tiles them, so the siblings
    /// after the child move at once, and recalculates its scroll range from where they now are. A Minimized element
    /// does nothing: its children are re-laid out when it is restored.
    /// </remarks>
    private void OnChildLayoutChanged()
    {
        if (_geometry.DisplayMode == ElementDisplayMode.Minimized)
        {
            return;
        }

        var tilesChildren = _childrenTileMode != ChildElementTileMode.Floating;
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            if (_layoutBatchDepth > 0 && tilesChildren)
            {
                Arrange();
            }

            MeasureAndArrangeNowOrDeferToLayoutBatch();
            return;
        }

        if (tilesChildren)
        {
            Arrange();
        }

        if (CanUserScrollVertical || CanUserScrollHorizontal)
        {
            RecalculateScrollBoundsFromChildren();
            SettleScrollbarsOutsideMeasure();
        }
    }

    /// <summary>
    /// Entry point for a full re-layout: measures this window's (and its subtree's) sizes
    /// bottom-up where needed, then arranges absolute positions/rectangles top-down. See
    /// Measure/Arrange below for why this is split into two passes rather than one.
    /// </summary>
    /// <remarks>
    /// Measures against exactly what the parent's own pass would offer (ContentSizeOfferedToChildren), so an element
    /// is the same size whichever side started the measure. A WrapContent parent's size depends on its children, so it
    /// is the same size whichever side started the measure. When its size changed, or it was attached or moved since,
    /// the parent hears of it (OnChildLayoutChanged). Nothing is measured under a Minimized parent: it has no content
    /// area to measure against.
    /// </remarks>
    internal void MeasureAndArrange()
    {
        if (_parent is { DisplayMode: ElementDisplayMode.Minimized })
        {
            return;
        }

        var previousSize = _geometry.CurrentSize;
        var availableSize = _parent is null
            ? MaximumSize
            : ComputeChildAvailableSize(_parent, this, _parent.ContentSizeOfferedToChildren());

        Measure(availableSize);
        Arrange();

        if (_parent is null || (_geometry.CurrentSize == previousSize && !_hasUnreportedLayoutChange))
        {
            return;
        }

        _hasUnreportedLayoutChange = false;
        _parent.OnChildLayoutChanged();
    }

    /// <summary>The content size this element's own Measure hands its children: when WrapContent, the most its content area can be within its cap (its own size isn't known until theirs is), else its content size.</summary>
    private Vector2 ContentSizeOfferedToChildren()
    {
        if (_geometry.DisplayMode == ElementDisplayMode.WrapContent)
        {
            var chromeSize = BorderInsetDoubled + ChildContentPaddingDoubled + new Vector2(0, WrapContentHeaderHeight) + ScrollbarGutterSize;
            return Vector2.Max(Vector2.Zero, EffectiveMaximumSize - chromeSize);
        }

        return _contentState.Size;
    }

    /// <summary>
    /// Per axis: a scrollable parent (see AddChildWindow/RecalculateScrollBoundsFromChildren)
    /// deliberately lets its children exceed its own content size on that axis -- overflow is
    /// meant to be revealed by scrolling, not clamped away -- so on that axis the child's own
    /// MaximumSize (its authored cap, else the screen) is the real constraint
    /// instead of the parent's visible content size. Otherwise a scrollable parent's
    /// children would be reclamped to its small content size on every Measure, whatever
    /// Layout.MaximumSize asked for.
    ///
    /// Shared by both MeasureAndArrange (asking "what's available to me from my parent?") and
    /// MeasureChildren (asking the same question for each of its own children while cascading a
    /// remeasure down the tree) -- so a cascading remeasure (an ancestor
    /// being resized) keeps a scrollable column's rows at their size and reveals less of them.
    /// </summary>
    private static Vector2 ComputeChildAvailableSize(Element parent, Element child, Vector2 parentContentSize)
    {
        // parentContentSize is parent.ContentSize (or, from Measure's own cascade, the equally
        // footer-shrunk _contentState.Size) -- already excludes parent.FooterHeight (see
        // RecalculateFixedSize/RecalculateFillSize). The footer host window itself (see
        // Window.SetFooterContent) is positioned at RelativePosition.Y == parent.ContentSize.Y,
        // i.e. exactly the boundary that exclusion creates -- without adding FooterHeight back
        // here, "remaining space" collapses to precisely zero for it every time, regardless of
        // its own requested height. Every other child's own DESIRED size already respects the
        // smaller, footer-excluded ContentSize on its own merits (every current consumer reads
        // ContentSize directly to decide what to ask for), so relaxing this shared cap doesn't
        // let one bleed into the footer band. Applied here,
        // not by each caller individually -- MeasureAndArrange (a child's own first measure,
        // called from Initialize/SetBounds) and Measure's own MeasureChildren cascade (an
        // already-attached child's later remeasure) are two separate call sites that both need
        // it.
        var effectiveParentContentSize = parentContentSize + new Vector2(0, parent.FooterHeight);
        var rawAvailableSize = effectiveParentContentSize - child._geometry.RelativePosition;
        var childMaximumSize = child.MaximumSize;
        return new Vector2(
            parent.CanUserScrollHorizontal ? childMaximumSize.X : rawAvailableSize.X,
            parent.CanUserScrollVertical ? childMaximumSize.Y : rawAvailableSize.Y);
    }

    /// <summary>
    /// Bottom-up size computation. WrapContent needs its children's sizes to compute its
    /// own, so it measures children first, offering them the most its content area can be
    /// within its own cap (the same resolution Android's View.measure() uses for WRAP_CONTENT,
    /// AT_MOST: a wrap-content node's own final size isn't known yet, so it offers children an
    /// upper bound rather than a size). Fixed/Fill/Minimized's
    /// own size never depends on children, so they compute themselves first and hand children
    /// their own now-final content size as the real constraint.
    /// </summary>
    private void Measure(Vector2 availableSize)
    {
        var previousSize = _geometry.CurrentSize;
        var previousContentSize = _contentState.Size;
        _geometry.AvailableSize = availableSize;

        if (_geometry.DisplayMode == ElementDisplayMode.Minimized)
        {
            // No MeasureChildren here -- a collapsed element has no real content area to
            // measure children against (RecalculateMinimizedSize deliberately zeroes
            // ContentSize), and re-measuring them against that zero region would clamp a
            // Fixed-mode child down to nothing, permanently corrupting it and, via
            // RetileChildren's position chaining, every later Vertical/Horizontal sibling too. Children simply
            // keep whatever size/position they already had -- Element.Initialize's own matching
            // guard means a child added while its parent is Minimized never had a real
            // measurement in the first place, so it just keeps its Build-time OriginalSize until
            // this element's next real (non-Minimized) Measure/Arrange pass reaches it.
            RecalculateMinimizedSize();
        }
        else
        {
            _measureDepth++;
            try
            {
                for (var settlePass = 0; ; settlePass++)
                {
                    MeasureOwnSizeAndChildren();

                    if (!AutoScrollbarVisibilityDiffers(out var showVertical, out var showHorizontal))
                    {
                        break;
                    }

                    if (settlePass == MaximumScrollbarSettlePasses)
                    {
                        Debug.Fail("Scrollbar visibility did not settle -- content grew as the content area widened.");
                        break;
                    }

                    _scrollbars.ShowVertical = showVertical;
                    _scrollbars.ShowHorizontal = showHorizontal;
                }
            }
            finally
            {
                _measureDepth--;
            }
        }

        if (_geometry.CurrentSize != previousSize)
        {
            Resized?.Invoke(this);
        }

        if (_contentState.Size != previousContentSize)
        {
            ContentResized?.Invoke(this);
        }
    }

    /// <summary>One pass of Measure for a non-Minimized element: its own size, its children, and its scroll bounds, all under the current scrollbar visibility.</summary>
    private void MeasureOwnSizeAndChildren()
    {
        switch (_geometry.DisplayMode)
        {
            case ElementDisplayMode.WrapContent:
                MeasureChildren(ContentSizeOfferedToChildren());
                RecalculateWrapContentSize();
                break;
            case ElementDisplayMode.Fixed:
                RecalculateFixedSize();
                MeasureChildren(_contentState.Size);
                break;
            case ElementDisplayMode.Fill:
                RecalculateFillSize();
                MeasureChildren(_contentState.Size);
                break;
            default:
                throw new NotImplementedException("No default display mode.");
        }

        // FooterHeight relaxation lives in ComputeChildAvailableSize itself (see its own doc
        // comment), not here -- both this cascade and MeasureAndArrange's own first-measure
        // call site route through it, so it only needs applying once.

        // Every measure, since this element or any of its children may have changed size in it.
        // Gated on having actual children: a
        // childless scrollable element (TextWindow/TextBox, scrolling wrapped text rather than
        // child Elements) already computes its own correct MaxScrollOffset from text metrics in
        // its own RecalculateFixedSize/RecalculateFillSize/RecalculateWrapContentSize override
        // (see TextWindow.UpdateScrollBounds) -- recalculating "from children" here as well
        // would just zero it back out, clobbering that already-correct value with the fact that
        // there's nothing to sum over.
        if ((CanUserScrollVertical || CanUserScrollHorizontal) && _children.Count > 0)
        {
            RecalculateScrollBoundsFromChildren();
        }
    }

    private void MeasureChildren(Vector2 availableContentSize)
    {
        foreach (var childElement in _children)
        {
            // An invisible child is excluded from measurement entirely -- the same
            // Visibility.Collapsed/View.GONE treatment RecalculateWrapContentSize and
            // RetileChildren give it, not just skipped when sizing this element around its
            // children. Becoming visible again measures it (the IsVisible setter), so nothing is
            // permanently stale once it reappears.
            if (!childElement.IsVisible)
            {
                continue;
            }

            childElement.Measure(ComputeChildAvailableSize(this, childElement, availableContentSize));
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

            // Relative to this element's own (already ContentPadding-inset) ContentAbsolutePosition
            // -- see RecalculateAbsolutePositions -- so the padding itself is applied exactly once,
            // uniformly for every child regardless of tile mode; this stays (0,0), not
            // ContentPadding, to avoid double-applying it here too.
            childElement._geometry.RelativePosition = previousVisibleChild is null
                ? new Vector2(0, 0)
                : _childrenTileMode == ChildElementTileMode.Horizontal
                    ? new Vector2(
                        previousVisibleChild._geometry.RelativePosition.X + previousVisibleChild._geometry.CurrentSize.X + previousVisibleChild._geometry.SpacingAfter,
                        previousVisibleChild._geometry.RelativePosition.Y)
                    : new Vector2(
                        previousVisibleChild._geometry.RelativePosition.X,
                        previousVisibleChild._geometry.RelativePosition.Y + previousVisibleChild._geometry.CurrentSize.Y + previousVisibleChild._geometry.SpacingAfter);

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

        _contentState.BackgroundAbsolutePosition = new Vector2(
            _geometry.AbsolutePosition.X + BorderInset.X,
            _geometry.AbsolutePosition.Y + BorderInset.Y + HeaderInsetHeight);
        _contentState.AbsolutePosition = _contentState.BackgroundAbsolutePosition + ChildContentPadding;

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
        _contentState.BackgroundSize = Vector2.Zero;

        var windowSize = _headerState.Size + BorderInsetDoubled;

        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(windowSize.X, _geometry.MinimumSize.X, EffectiveMaximumSize.X),
            windowSize.Y);
    }

    protected virtual void RecalculateFixedSize()
    {
        var effectiveMaximumSize = EffectiveMaximumSize;
        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(_geometry.OriginalSize.X, _geometry.MinimumSize.X, effectiveMaximumSize.X),
            MathHelper.Clamp(_geometry.OriginalSize.Y, _geometry.MinimumSize.Y, effectiveMaximumSize.Y));

        // Resize horizontally to fit the new element's size, but keep the vertical size.
        _headerState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _headerState.OriginalSize.Y - BorderInset.Y);

        // Content sits below the header (which itself already accounts for just the top
        // border) and above the bottom border, so its own height must clear both the top and
        // bottom border -- BorderInsetDoubled.Y, not BorderInset.Y. Getting this wrong left no
        // room for a bottom border strip, so content's own background fill (drawn after the
        // border in Draw()) painted directly over it.
        _contentState.BackgroundSize = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _geometry.CurrentSize.Y - BorderInsetDoubled.Y - HeaderInsetHeight);
        _contentState.Size = _contentState.BackgroundSize - ChildContentPaddingDoubled - new Vector2(0, FooterHeight) - ScrollbarGutterSize;
    }

    protected virtual void RecalculateFillSize()
    {
        _geometry.CurrentSize = EffectiveMaximumSize;

        _headerState.Size = new Vector2(
            _geometry.CurrentSize.X - BorderInsetDoubled.X,
            _headerState.OriginalSize.Y - BorderInset.Y);

        _contentState.BackgroundSize = _geometry.CurrentSize
            - (_headerState.ShowHeader
                ? _headerState.Size
                : Vector2.Zero)
            - BorderInsetDoubled;
        _contentState.Size = _contentState.BackgroundSize - ChildContentPaddingDoubled - new Vector2(0, FooterHeight) - ScrollbarGutterSize;
    }

    protected virtual void RecalculateWrapContentSize()
    {
        _contentState.Size = _canContainChildren ? ChildrenExtent() : Vector2.Zero;

        // Children's own RelativePosition values are relative to this element's ContentAbsolutePosition,
        // which is ALREADY inset by ChildContentPadding (see RecalculateAbsolutePositions) -- so
        // ChildrenExtent measures only the children's own footprint, with no padding baked
        // in yet. The background adds ChildContentPaddingDoubled (gated on CanContainChildren alone,
        // not "and has a child right now" -- see its own doc comment) for both that leading inset and
        // a matching trailing gap, even while still childless, so a childless CanContainChildren
        // element (e.g. Folder before its first child is added) never has to size its header (see
        // MinimumHeaderWidth) from a literal (0,0) background. It also adds the scrollbars shown.
        var scrollbarGutterSize = ScrollbarGutterSize;
        var backgroundSize = _contentState.Size + ChildContentPaddingDoubled + scrollbarGutterSize;

        // A WrapContent element must be at least as wide as its header needs (e.g. Window's
        // title text + buttons -- see MinimumHeaderWidth), not just its content.
        if (_headerState.ShowHeader)
        {
            backgroundSize.X = System.Math.Max(backgroundSize.X, MinimumHeaderWidth());
        }

        // Never larger than its own cap leaves after the border and header; content past it is
        // clipped, and scrolls where the element allows scrolling.
        var wrapContentHeaderHeight = WrapContentHeaderHeight;
        backgroundSize = Vector2.Min(backgroundSize, Vector2.Max(Vector2.Zero, EffectiveMaximumSize - BorderInsetDoubled - new Vector2(0, wrapContentHeaderHeight)));

        // Size is the area children occupy -- padding and scrollbars excluded, as for Fixed and Fill.
        _contentState.BackgroundSize = backgroundSize;
        _contentState.Size = Vector2.Max(Vector2.Zero, backgroundSize - ChildContentPaddingDoubled - scrollbarGutterSize);

        _geometry.CurrentSize = backgroundSize;
        if (_headerState.ShowHeader)
        {
            _headerState.Size = new Vector2(backgroundSize.X, wrapContentHeaderHeight);
            _geometry.CurrentSize.Y += wrapContentHeaderHeight;
        }
        _geometry.CurrentSize += BorderInsetDoubled;
    }

    /// <summary>The header height RecalculateWrapContentSize gives a WrapContent element: its built height less the top border, or zero with no header.</summary>
    private float WrapContentHeaderHeight => _headerState.ShowHeader ? _headerState.OriginalSize.Y - BorderInset.Y : 0f;

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
        _contentState.BackgroundRectangle = new Rectangle((int)_contentState.BackgroundAbsolutePosition.X, (int)_contentState.BackgroundAbsolutePosition.Y, (int)_contentState.BackgroundSize.X, (int)_contentState.BackgroundSize.Y);
        _viewport = new Viewport(_contentState.Rectangle);

        RecalculateBorderRectangles();
        RecalculateScrollbarRectangles();
    }

    /// <summary>Tracks run along the right and bottom of the content background, above any footer; thumbs follow ScrollOffset, so this runs on every Arrange, including the one ScrollBy triggers.</summary>
    private void RecalculateScrollbarRectangles()
    {
        var thickness = (int)WindowChrome.ScrollbarThickness;
        var background = _contentState.BackgroundRectangle;
        var trackBottom = background.Bottom - (int)FooterHeight;

        if (_scrollbars.ShowVertical)
        {
            var trackHeight = trackBottom - background.Y - (_scrollbars.ShowHorizontal ? thickness : 0);
            var track = new Rectangle(background.Right - thickness, background.Y, thickness, System.Math.Max(0, trackHeight));
            var (thumbStart, thumbLength) = ComputeScrollbarThumbSpan(track.Y, track.Height, _contentState.Size.Y, _maxScrollOffset.Y, _scrollOffset.Y);
            _scrollbars.VerticalTrackRectangle = track;
            _scrollbars.VerticalThumbRectangle = new Rectangle(track.X, thumbStart, thickness, thumbLength);
        }
        else
        {
            _scrollbars.VerticalTrackRectangle = Rectangle.Empty;
            _scrollbars.VerticalThumbRectangle = Rectangle.Empty;
        }

        if (_scrollbars.ShowHorizontal)
        {
            var trackWidth = background.Width - (_scrollbars.ShowVertical ? thickness : 0);
            var track = new Rectangle(background.X, trackBottom - thickness, System.Math.Max(0, trackWidth), thickness);
            var (thumbStart, thumbLength) = ComputeScrollbarThumbSpan(track.X, track.Width, _contentState.Size.X, _maxScrollOffset.X, _scrollOffset.X);
            _scrollbars.HorizontalTrackRectangle = track;
            _scrollbars.HorizontalThumbRectangle = new Rectangle(thumbStart, track.Y, thumbLength, thickness);
        }
        else
        {
            _scrollbars.HorizontalTrackRectangle = Rectangle.Empty;
            _scrollbars.HorizontalThumbRectangle = Rectangle.Empty;
        }

        _scrollbars.CornerRectangle = _scrollbars.ShowVertical && _scrollbars.ShowHorizontal
            ? new Rectangle(background.Right - thickness, trackBottom - thickness, thickness, thickness)
            : Rectangle.Empty;
    }

    /// <summary>Start and length of a scrollbar thumb along its track: length proportional to how much of the content is visible (never below ScrollbarMinimumThumbLength), position proportional to how far it's scrolled.</summary>
    internal static (int Start, int Length) ComputeScrollbarThumbSpan(int trackStart, int trackLength, float visibleLength, float maxScrollOffset, float scrollOffset)
    {
        if (trackLength <= 0)
        {
            return (trackStart, 0);
        }

        if (maxScrollOffset <= 0f)
        {
            return (trackStart, trackLength);
        }

        var clampedVisibleLength = System.Math.Max(0f, visibleLength);
        var proportionalLength = trackLength * clampedVisibleLength / (clampedVisibleLength + maxScrollOffset);
        var minimumLength = System.Math.Min(WindowChrome.ScrollbarMinimumThumbLength, trackLength);
        var thumbLength = (int)MathF.Round(System.Math.Clamp(proportionalLength, minimumLength, trackLength));
        var scrolledFraction = System.Math.Clamp(scrollOffset / maxScrollOffset, 0f, 1f);
        var thumbStart = trackStart + (int)MathF.Round((trackLength - thumbLength) * scrolledFraction);

        return (thumbStart, thumbLength);
    }

    private void RecalculateBorderRectangles()
    {
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(_geometry.Rectangle, _border.Thickness);
        _border.TopRectangle = top;
        _border.BottomRectangle = bottom;
        _border.LeftRectangle = left;
        _border.RightRectangle = right;
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
        SetRelativePositionField(relativePosition);
        MeasureAndArrange();
    }

    /// <summary>Writes RelativePosition, noting a real move so the next MeasureAndArrange reports it to the parent (its scroll range and wrap-around depend on where children are).</summary>
    private void SetRelativePositionField(Vector2 relativePosition)
    {
        if (_geometry.RelativePosition != relativePosition)
        {
            _geometry.RelativePosition = relativePosition;
            _hasUnreportedLayoutChange = true;
        }
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

    /// <summary>Sets the authored cap after construction, replacing Layout.MaximumSize.</summary>
    /// <remarks>Measure never changes it. Does not itself re-measure -- call SetSize afterward if the current size also needs to follow the new cap.</remarks>
    public void SetMaximumSize(Vector2 maximumSize) => _geometry.AuthoredMaximumSize = maximumSize;

    /// <summary>
    /// Sets relative position and Fixed-mode size together in one MeasureAndArrange pass --
    /// needed for left/top-edge resize, which must move position and
    /// size together to keep the opposite edge visually fixed. A separate SetSize then
    /// SetRelativePosition would relayout twice and fire Resized then Moved from two calls
    /// instead of one.
    /// </summary>
    public void SetBounds(Vector2 relativePosition, Vector2 size)
    {
        SetRelativePositionField(relativePosition);
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
    /// to solve, just automated. A Window whose _content isn't cleared on pool-return would keep
    /// driving its previous use's content -- see Window's own override.
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

    /// <summary>True from the moment Close starts until the element is back in its pool.</summary>
    private bool _isClosing;

    /// <summary>Raises Closed, then returns the element to its pool.</summary>
    /// <remarks>
    /// A Close reached again from inside the Closed handlers is a no-op: closing it there would pool the element while its
    /// handlers still run, a handler could rent it again as a new window, and the outer Close would then pool that live
    /// window, leaving it in its layer with no handler left to remove it.
    /// </remarks>
    public void Close()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        try
        {
            Closed?.Invoke(this);
            ElementPoolService.CloseElement(this);
        }
        finally
        {
            _isClosing = false;
        }
    }
}
