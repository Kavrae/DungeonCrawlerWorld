using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ChromeBehaviors;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// An Element with a text title bar (Element's generic header, specialized to draw text+
/// buttons instead of e.g. Folder's icon), pluggable IElementContent, and close/minimize chrome.
/// </summary>
public class Window : Element
{
    /// <summary>
    /// Only Window supports pluggable arbitrary content -- Folder's content area is always its
    /// children, Button has none -- so this (and IElementContent.Initialize's Window-typed
    /// parameter) lives here rather than on Element.
    /// </summary>
    private IElementContent? _content;

    /// <summary>
    /// Public so a hit-test/draw result can be checked for a specific IElementContent, as
    /// UiInputController's content-drag path does.
    /// </summary>
    public IElementContent? Content => _content;

    /// <summary>Pluggable content for the reserved footer band (see Element.FooterHeight) -- set via SetFooterContent, hosted in _footerHostWindow once ContentSize is real (see OnChildrenInitialized).</summary>
    private IElementContent? _footerContent;

    /// <summary>Borderless/titleless child window occupying the reserved footer band -- an ordinary AddChild descendant, not a header-style escape hatch (see Element.FooterHeight's own doc comment for why). Null until OnChildrenInitialized runs, and only ever created if SetFooterContent was called first.</summary>
    private Window? _footerHostWindow;

    public IElementContent? FooterContent => _footerContent;

    /// <summary>
    /// Window's own OnClosed override -- see Element.OnClosed's own doc comment: a pooled Window must
    /// not keep driving its old content once it is reused. Calls base.OnClosed() first so Element's own OnRightClicked clearing
    /// still happens for every Window too, not just plain Elements.
    /// </summary>
    protected internal override void OnClosed()
    {
        base.OnClosed();
        _content = null;
        _footerContent = null;
        _footerHostWindow = null;
        FooterHeight = 0f;
    }

    /// <summary>
    /// Internal, not protected: chrome behaviors (see IWindowChromeBehavior) live outside
    /// the Window subclass hierarchy but still need the window's title font to build
    /// matching title buttons.
    /// </summary>
    internal SpriteFontBase TitleFont { get; }

    /// <summary>Measures TitleFont for truncating the title -- FontService's one measurer for that font, since TitleFont itself never changes after construction (see TitleFont's own doc comment).</summary>
    private readonly FontStashTextMeasurer _titleFontMeasurer;

    /// <summary>The title as drawn, shortened with "..." to fit the header (StringUtility.TruncateWithEllipsis) -- truncating measures a prefix per probe and builds a string, so it's redone only when the title or the room for it changes.</summary>
    private readonly KeyedCache<(string Title, float AvailableWidth), string> _drawnTitle = new();

    /// <summary>The geometry title (the title plus this window's position and size, WindowDebugChrome), rebuilt only when one of those changes -- see GeometryTitleText.</summary>
    private readonly KeyedCache<(string Title, Vector2 Position, Vector2 Size), string> _geometryTitle = new();

    public Vector2 TitlePadding { get; set; } = new(5, 2);

    private string _titleText = string.Empty;

    /// <remarks>A window whose width follows its title (WrapContent, Minimized) is laid out again when the title changes.</remarks>
    public string TitleText
    {
        get => _titleText;
        set
        {
            if (value == _titleText)
            {
                return;
            }

            _titleText = value;

            if (DisplayMode is ElementDisplayMode.WrapContent or ElementDisplayMode.Minimized)
            {
                MeasureAndArrange();
            }
        }
    }

    private Color _titleColor;
    public Color TitleColor => _titleColor;

    private Color _titleTextColor;
    public Color TitleTextColor => _titleTextColor;

    /// <summary>This window's own border color when unfocused -- captured at Build time (see Build) so Update can restore it once IsFocused clears, instead of the focused accent (see FocusAccentColor) permanently clobbering a window's own custom BorderColor.</summary>
    private Color _unfocusedBorderColor;

    private List<Button> _titleButtons = [];

    /// <summary>The buttons on this window's title bar, rightmost first -- added only through AddTitleButton.</summary>
    public IReadOnlyList<Button> TitleButtons => _titleButtons;

    /// <summary>
    /// Close/minimize-via-title-button are Window-specific, not generic Element capabilities:
    /// CloseBehavior/MinimizeRestoreBehavior both bind to a concrete Window (they build a
    /// Button attached to *its* title bar via AddTitleButton) -- an Element with no title bar
    /// at all (Folder, Button) has nowhere for that button to attach, so these flags and the
    /// behavior-attachment they drive live here instead of on Element.
    /// </summary>
    public bool CanUserClose { get; set; }

    public bool CanUserMinimize { get; set; }

    public Window(FontService fontService, ElementPoolService windowService, UiRenderService renderService)
        : base(fontService, windowService, renderService)
    {
        TitleFont = fontService.GetFont(FontChrome.WindowTitleFontSize);
        _titleFontMeasurer = fontService.GetTextMeasurer(TitleFont);
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        var chrome = options.Chrome;

        _titleText = chrome?.TitleText ?? string.Empty;
        _titleColor = chrome?.TitleColor ?? WindowPalette.HeaderBackground;
        _titleTextColor = chrome?.TitleTextColor ?? WindowPalette.TitleTextColor;
        _titleButtons = [];

        // Captured after base.Build has resolved _border.Color (chrome?.BorderColor ??
        // WindowPalette.BorderColor) -- Update restores this once IsFocused clears, rather than
        // FocusAccentColor permanently overwriting whatever this window's own border color is.
        _unfocusedBorderColor = _border.Color;

        CanUserClose = chrome?.CanUserClose ?? false;
        CanUserMinimize = chrome?.CanUserMinimize ?? false;

        _headerState.OriginalSize = new Vector2(_geometry.OriginalSize.X, TitleFont.MeasureString(" ").Y + TitlePadding.Y * 3);
        _headerState.Size = _headerState.OriginalSize;
    }

    public override void Initialize()
    {
        base.Initialize();

        if (ShowHeader)
        {
            // Close/minimize/restore are the standard, near-universal chrome capabilities, so
            // Window still decides whether to attach them from the existing options flags.
            // Anything else -- including move/resize/dock once built -- attaches via
            // AddChromeBehavior from outside Window, which never needs to know they exist.
            // Close is attached first (and so ends up rightmost, per AddTitleButton's
            // right-to-left insertion order) so the standard layout is always
            // [minimize/restore] [close] with both grouped on the title bar's right side.
            if (CanUserClose)
            {
                AddChromeBehavior(new CloseBehavior());
            }

            if (CanUserMinimize)
            {
                AddChromeBehavior(new MinimizeRestoreBehavior());
            }
        }
    }

    protected override void OnChildrenInitialized()
    {
        _content?.Initialize(this);

        if (_footerContent is null)
        {
            return;
        }

        CreateFooterHostWindow(_footerContent);

        // Keeps the footer host pinned to the bottom of ContentSize whenever it changes --
        // ContentResized, not Resized, since a scrollbar gutter changes ContentSize without
        // resizing the window. No manual unsubscribe needed: ElementPoolService.CloseElement
        // clears every event on pool-return.
        ContentResized += PinFooterHostWindow;
    }

    private void CreateFooterHostWindow(IElementContent footerContent)
    {
        _footerHostWindow = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, ContentSize.Y), Size = new Vector2(ContentSize.X, FooterHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        // Pass-through host -- its own children must sit flush against the footer band, not
        // inset a second time on top of this window's own ContentPadding (see
        // GridControl.Build/TabbedContent's _tabHeaderWindow for the same convention).
        _footerHostWindow.ContentPadding = Vector2.Zero;
        _footerHostWindow.SetContent(footerContent);
        AddChild(_footerHostWindow);
    }

    private void PinFooterHostWindow(Element _) =>
        _footerHostWindow?.SetBounds(new Vector2(0, ContentSize.Y), new Vector2(ContentSize.X, FooterHeight));

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        // Header background no longer swaps on focus (see DrawHeader) -- shown as a border accent
        // instead, resolved here rather than in Build since IsActiveWindow changes every frame
        // while Build only ever runs once (or once per pooled reuse). IsActiveWindow, not
        // IsFocused -- most windows' actual keyboard-focus target is a title button or content
        // TextBox, not the Window itself, and clicking non-focusable content (most rows/cells)
        // clears keyboard focus entirely (see IsActiveWindow's own doc comment).
        _border.Color = _isActiveWindow ? WindowPalette.FocusAccentColor : _unfocusedBorderColor;

        _content?.Update(gameTime);
    }

    public override void DrawContent(GameTime gameTime) => _content?.DrawContent(gameTime);

    protected override void OnKeyPressAction(Keys key) => _content?.HandleKeyPress(key);

    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState) => _content?.HandleHotkeys(keyboardState, previousKeyboardState);

    protected override void OnTextInputAction(char character) => _content?.HandleTextInput(character);

    /// <summary>
    /// Attaches what this window draws in its content area (see IElementContent), instead of
    /// subclassing Window and overriding DrawContent. Must be called before Initialize --
    /// content's own Initialize(this) runs as part of Window.Initialize() (see OnChildrenInitialized).
    ///
    /// Defensively clears this window's own children first -- the true, single choke point every
    /// IElementContent.Initialize(hostWindow) call is preceded by, whether the very first
    /// attachment (before the window's own Initialize ever runs) or
    /// a content swap on an already-long-lived window that never gets rebuilt through the pool
    /// again (TabbedContent.SwitchTab calling this on the same _bodyWindow every tab activation).
    /// A no-op in the ordinary case (children already properly closed by whatever ran before
    /// this), but a hard structural guarantee against two contents' children ever coexisting
    /// regardless of what upstream sequence produced the call -- the same "generalize the fix to
    /// the shared choke point, not per-widget" reasoning ElementPoolService.CloseElement's own
    /// event-clearing follows for pool returns. The footer host is one of those children, so a
    /// window already hosting its footer builds a fresh host for it.
    /// </summary>
    public void SetContent(IElementContent content)
    {
        var footerContentToRehost = _footerHostWindow is null ? null : _footerContent;

        ElementPoolService.CloseAllChildren(this);
        _footerHostWindow = null;
        _content = content;

        if (footerContentToRehost is not null)
        {
            CreateFooterHostWindow(footerContentToRehost);
        }
    }

    /// <summary>
    /// Reserves a fixed-height band at the bottom of this window's content area for content,
    /// hosted in its own borderless/titleless child window (see OnChildrenInitialized) once
    /// ContentSize is real -- must be called before Initialize, same contract as SetContent.
    /// Only records intent here: FooterHeight must already be set before this window's own first
    /// MeasureAndArrange (during Initialize) for ContentSize to come out correctly shrunk from
    /// the start, but the footer host window itself can't be created until ContentSize is final,
    /// which for a window that computes its own final size in OnChildrenInitialized is later than Configure.
    /// </summary>
    public void SetFooterContent(IElementContent content, float height)
    {
        _footerContent = content;
        FooterHeight = height;
    }

    /// <summary>Attaches a chrome capability (see IChromeBehavior) to this window.</summary>
    public void AddChromeBehavior(IChromeBehavior behavior)
    {
        behavior.Attach(this);
    }

    // No InitializeHeaderExtras override: title buttons are attached by a chrome behavior's own
    // AddChromeBehavior call, always after base.Initialize() (which is what invokes
    // InitializeHeaderExtras, before this override's own continuation ever runs) -- so
    // _titleButtons is provably always empty by the time it would fire, for every current call
    // site (a behavior attaches its buttons from within this override's own body, or later).
    // Deliberately not "harmless dead code left in for symmetry" either: Button's own Initialize()
    // is inherited, unmodified Element.Initialize(), which would call MeasureAndArrange() and
    // (since a title button's _parent is null, see PositionInHeader's own doc comment) reset its
    // AbsolutePosition straight to its own RelativePosition -- actively wrong for a header-relative
    // button, undoing whatever PositionInHeader already established. A title button's real
    // initialization is PositionInHeader, called synchronously from AddTitleButton/
    // RepositionTitleButtons -- never Initialize.

    protected override void UpdateHeaderExtras(GameTime gameTime)
    {
        foreach (var button in _titleButtons)
        {
            // Same "a hidden element doesn't keep ticking" gate Element.Update already applies to
            // ordinary _children.
            if (!button.IsVisible)
            {
                continue;
            }

            button.Update(gameTime);
        }
    }

    protected override void DrawHeader(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (!_isTransparent)
        {
            spriteBatch.Draw(unitRectangle, HeaderRectangle, _titleColor);
        }
        var titleText = WindowDebugChrome.ShowsGeometryInTitles ? GeometryTitleText() : _titleText;

        // Most windows are sized to fit their own title (see MinimumHeaderWidth), so this is a
        // no-op for them -- but a fixed-width popup (Tooltip.UseFixedWidth) can be
        // handed a title wider than its own header, so it is truncated with an ellipsis.
        var availableTextWidth = HeaderSize.X - TitlePadding.X * 2 - TotalTitleButtonsWidth();
        var drawnTitleText = _drawnTitle.Get((titleText, availableTextWidth), _titleFontMeasurer, static (key, measurer) => StringUtility.TruncateWithEllipsis(measurer, key.Title, key.AvailableWidth));

        // Routed through LabelRenderer.Draw, not a direct DrawString call, so the title bar gets
        // the same whole-pixel-rounding fix every other UI text draw does -- see its own doc
        // comment for the "bottom of the text is cut off" bug this closes.
        LabelRenderer.Draw(spriteBatch, TitleFont, drawnTitleText, HeaderAbsolutePosition + TitlePadding, _titleTextColor);

        foreach (var button in _titleButtons)
        {
            button.Draw(gameTime);
        }
    }

    // Uses the shared hit-test helper (see FindTopmostHit's own doc comment) rather than a
    // manual foreach -- also sidesteps a reentrancy hazard a manual foreach here would have: a
    // title button's own Clicked handler can close its window (e.g. the close button) before
    // this method returns, and ElementPoolService.CloseElement pool-returns every title button
    // on close, so an in-progress plain `foreach (var button in _titleButtons)` over that same
    // list would throw. FindTopmostHit finishes reading the list before HandleClick ever runs,
    // so there's nothing left to corrupt by the time a click handler mutates anything.
    protected override void OnHeaderClickAction(Point mousePosition) => FindTopmostHit(_titleButtons, mousePosition)?.HandleClick(mousePosition);

    protected override Button? FindHeaderButtonAt(Point position) => ShowHeader ? FindTopmostHit(_titleButtons, position) as Button : null;

    /// <summary>Inset from the title bar's own height a title button's default square size shrinks by, leaving a small margin above/below it -- shared by every chrome behavior that builds a standard-looking title button.</summary>
    private const float DefaultTitleButtonSizeInset = 4;

    /// <summary>Default square size for a title button, derived from this window's own title bar height -- see DefaultTitleButtonSizeInset.</summary>
    internal static Vector2 DefaultTitleButtonSize(Window window)
    {
        var side = window.OriginalHeaderSize.Y - DefaultTitleButtonSizeInset;
        return new Vector2(side, side);
    }

    /// <summary>
    /// Builds a standard-sized/fonted title button, not yet attached (the caller still calls
    /// AddTitleButton itself, after wiring whatever Clicked/label logic is actually its own) --
    /// shared by every IChromeBehavior that builds one, a host's included, since construction, sizing and
    /// font are the same for all of them and only the click handler differs.
    /// </summary>
    public static Button BuildTitleButton(Window window, string? text = null)
    {
        var button = window.ElementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = DefaultTitleButtonSize(window) },
            Text = text is null ? null : new TextOptions { Text = text },
        });
        button.ContentFont = window.TitleFont;
        return button;
    }

    /// <summary>
    /// Attaches a Button to this window's title bar -- a title button is constructed with
    /// parent: null (see the 3 IChromeBehavior implementations, the only callers) and never
    /// added to _children: it lives in the header zone, not the content zone (see
    /// Element.HandleClick/TryHitTestInteraction's own doc comments for why those two zones are
    /// resolved by different code paths), so the ordinary parent-relative Measure/Arrange
    /// pipeline -- which positions everything relative to a parent's *content* area -- would
    /// place it wrong. RepositionTitleButtons instead calls Button.PositionInHeader directly with
    /// this window's own AbsolutePosition, the header-relative equivalent of what
    /// SetRelativePosition/MeasureAndArrange do for an ordinary parented child.
    /// </summary>
    public void AddTitleButton(Button newButton, int? insertIndex = null)
    {
        if (!ShowHeader)
        {
            ElementPoolService.CloseElement(newButton);
            return;
        }

        var maximumIndex = _titleButtons.Count;
        var clampedInsertIndex = System.Math.Clamp(insertIndex ?? maximumIndex, 0, maximumIndex);

        _titleButtons.Insert(clampedInsertIndex, newButton);

        RepositionTitleButtons();

        if (DisplayMode == ElementDisplayMode.WrapContent)
        {
            MeasureAndArrange();
        }
    }

    /// <summary>
    /// Right-aligns title buttons against the current title width, tiling each earlier-added
    /// button further left of the one after it -- re-run on every recalculation (see
    /// RecalculateHeaderExtras, not just once at attach time), since minimizing shrinks the
    /// title bar to fit just its text. Without this, a button's cached relative position
    /// (computed against the window's full static width) would drift outside the shrunk title
    /// bar once minimized, making it unclickable exactly when it's needed to restore the window.
    /// </summary>
    private void RepositionTitleButtons()
    {
        for (var index = 0; index < _titleButtons.Count; index++)
        {
            var button = _titleButtons[index];
            if (index == 0)
            {
                button.PositionInHeader(new Vector2(HeaderSize.X - button.CurrentSize.X - 3, 3), AbsolutePosition);
            }
            else
            {
                var previousButton = _titleButtons[index - 1];
                button.PositionInHeader(new Vector2(
                    previousButton.RelativePosition.X - previousButton.CurrentSize.X - 3,
                    previousButton.RelativePosition.Y), AbsolutePosition);
            }
        }
    }

    protected override void RecalculateHeaderExtras() => RepositionTitleButtons();

    protected override void RecalculateMinimizedSize()
    {
        // MinimumHeaderWidth already accounts for the title buttons (close/minimize-restore)
        // alongside the text -- a short/empty title would otherwise shrink the title bar
        // narrower than the buttons it still has to hold, and RepositionTitleButtons (which
        // doesn't know about text width, only _header.Size.X) would tile them overlapping each
        // other or the text.
        var headerWidth = MinimumHeaderWidth();
        ApplyMinimizedSize(headerWidth, MathHelper.Clamp(headerWidth + BorderInsetDoubled.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X));
    }

    /// <summary>Collapses this window to its title bar: a header headerWidth wide and as tall as the expanded title bar, the window windowWidth wide and as tall as the header plus its border.</summary>
    /// <remarks>For a RecalculateMinimizedSize override that sizes the collapsed bar its own way.</remarks>
    protected void ApplyMinimizedSize(float headerWidth, float windowWidth)
    {
        _headerState.Size = new Vector2(headerWidth, _headerState.OriginalSize.Y);
        _contentState.Size = Vector2.Zero;
        _contentState.BackgroundSize = Vector2.Zero;
        _geometry.CurrentSize = new Vector2(windowWidth, _headerState.Size.Y + BorderInsetDoubled.Y);
    }

    /// <summary>The title with this window's position and size after it, as WindowDebugChrome shows it -- rebuilt only when the title, position or size changes, not every frame.</summary>
    private string GeometryTitleText() =>
        _geometryTitle.Get((_titleText, RelativePosition, CurrentSize), static key => $"{key.Title} ({key.Position.X:0}, {key.Position.Y:0}) {key.Size.X:0}x{key.Size.Y:0}");

    /// <summary>Total width every title button needs, tiled with the same 3px gaps RepositionTitleButtons itself uses -- see MinimumHeaderWidth, which combines this with the title text's own width.</summary>
    private float TotalTitleButtonsWidth()
    {
        if (_titleButtons.Count == 0)
        {
            return 0f;
        }

        var width = 3f; // gap between the rightmost button and the title's right edge.
        foreach (var button in _titleButtons)
        {
            width += button.CurrentSize.X + 3f; // each button plus the gap to its left.
        }

        return width;
    }

    /// <summary>
    /// Natural width the title bar needs for its own text plus buttons, independent of
    /// content. Summed, not maxed: the text (left-aligned) and buttons (right-aligned,
    /// RepositionTitleButtons) both draw within the same title bar simultaneously, so a title
    /// bar sized to fit only whichever is bigger doesn't actually have room for both -- the
    /// buttons, drawn on top, would cover the tail of the text.
    /// </summary>
    protected override float MinimumHeaderWidth()
    {
        var textSize = TitleFont.MeasureString(_titleText);
        return textSize.X + TitlePadding.X * 2 + TotalTitleButtonsWidth();
    }
}
