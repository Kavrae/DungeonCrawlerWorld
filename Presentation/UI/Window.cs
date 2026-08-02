using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ChromeBehaviors;

namespace Presentation.UI;

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
    /// Internal, not protected: chrome behaviors (see IWindowChromeBehavior) live outside
    /// the Window subclass hierarchy but still need the window's title font to build
    /// matching title buttons.
    /// </summary>
    internal SpriteFontBase TitleFont { get; }

    public Vector2 TitlePadding { get; set; } = new(5, 2);

    private string _titleText = string.Empty;
    public string TitleText { get => _titleText; set => _titleText = value; }

    private Color _titleColor;
    public Color TitleColor => _titleColor;

    private Color _focusedTitleColor;
    public Color FocusedTitleColor => _focusedTitleColor;

    private List<Button> _titleButtons = [];
    public List<Button> TitleButtons { get => _titleButtons; set => _titleButtons = value; }

    /// <summary>
    /// Close/minimize-via-title-button are Window-specific, not generic Element capabilities:
    /// CloseBehavior/MinimizeRestoreBehavior both bind to a concrete Window (they build a
    /// Button attached to *its* title bar via AddTitleButton) -- an Element with no title bar
    /// at all (Folder, Button) has nowhere for that button to attach, so these flags and the
    /// behavior-attachment they drive live here instead of on Element.
    /// </summary>
    public bool CanUserClose { get; set; }

    public bool CanUserMinimize { get; set; }

    /*========Compat aliases over Element's generic header========*/
    public bool ShowTitle => ShowHeader;
    public bool ShowTitleWhenMinimized => ShowHeaderWhenMinimized;
    public Vector2 OriginalTitleSize => OriginalHeaderSize;
    public Vector2 TitleSize => HeaderSize;
    public Vector2 TitleAbsolutePosition => HeaderAbsolutePosition;
    public Rectangle TitleRectangle => HeaderRectangle;

    public Window(FontService fontService, ElementPoolService windowService, GlyphRenderer glyphRenderer)
        : base(fontService, windowService, glyphRenderer)
    {
        TitleFont = fontService.GetFont(8);
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        var chrome = options.Chrome;

        _titleText = chrome?.TitleText ?? string.Empty;
        _titleColor = chrome?.TitleColor ?? Color.LightBlue;
        _focusedTitleColor = chrome?.FocusedTitleColor ?? Color.Gold;
        _titleButtons = [];

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

    protected override void OnChildrenInitialized() => _content?.Initialize(this);

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        _content?.Update(gameTime);
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) => _content?.DrawContent(gameTime, spriteBatch, unitRectangle);

    protected override void OnKeyPressAction(Keys key) => _content?.HandleKeyPress(key);

    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState) => _content?.HandleHotkeys(keyboardState, previousKeyboardState);

    protected override void OnTextInputAction(char character) => _content?.HandleTextInput(character);

    /// <summary>
    /// Attaches what this window draws in its content area (see IElementContent), instead of
    /// subclassing Window and overriding DrawContent. Must be called before Initialize --
    /// content's own Initialize(this) runs as part of Window.Initialize() (see OnChildrenInitialized).
    /// </summary>
    public void SetContent(IElementContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
    }

    /// <summary>Attaches a chrome capability (see IChromeBehavior) to this window.</summary>
    public void AddChromeBehavior(IChromeBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);

        behavior.Attach(this);
    }

    protected override void InitializeHeaderExtras()
    {
        foreach (var button in _titleButtons)
        {
            button.Initialize();
        }
    }

    protected override void UpdateHeaderExtras(GameTime gameTime)
    {
        foreach (var button in _titleButtons)
        {
            button.Update(gameTime);
        }
    }

    protected override void DrawHeader(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!_isTransparent)
        {
            var titleBackgroundColor = _isFocused
                ? _focusedTitleColor
                : _titleColor;
            spriteBatch.Draw(unitRectangle, HeaderRectangle, titleBackgroundColor);
        }
        spriteBatch.DrawString(TitleFont, _titleText, HeaderAbsolutePosition + TitlePadding, Color.Black);

        foreach (var button in _titleButtons)
        {
            button.Draw(gameTime, spriteBatch, unitRectangle);
        }
    }

    protected override void OnHeaderClickAction(Point mousePosition)
    {
        foreach (var button in _titleButtons)
        {
            if (button.Rectangle.Contains(mousePosition))
            {
                button.HandleClick(mousePosition);
            }
        }
    }

    protected override Button? FindHeaderButtonAt(Point position)
    {
        if (!ShowHeader)
        {
            return null;
        }

        foreach (var button in _titleButtons)
        {
            if (button.Rectangle.Contains(position))
            {
                return button;
            }
        }

        return null;
    }

    public void AddTitleButton(Button newButton, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(newButton);

        if (!ShowHeader)
        {
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
                button.ChangeRelativePosition(new Vector2(TitleSize.X - button.CurrentSize.X - 3, 3));
            }
            else
            {
                var previousButton = _titleButtons[index - 1];
                button.ChangeRelativePosition(new Vector2(
                    previousButton.RelativePosition.X - previousButton.CurrentSize.X - 3,
                    previousButton.RelativePosition.Y));
            }
        }
    }

    protected override void RecalculateHeaderExtras() => RepositionTitleButtons();

    protected override void RecalculateMinimizedSize()
    {
        var textSize = TitleFont.MeasureString(_titleText);

        // MinimumHeaderWidth already accounts for the title buttons (close/minimize-restore)
        // alongside the text -- a short/empty title would otherwise shrink the title bar
        // narrower than the buttons it still has to hold, and RepositionTitleButtons (which
        // doesn't know about text width, only _header.Size.X) would tile them overlapping each
        // other or the text.
        _headerState.Size = new Vector2(MinimumHeaderWidth(), textSize.Y + TitlePadding.Y * 2);

        _contentState.Size = new Vector2(0, 0);

        var windowSize = _headerState.Size + BorderInsetDoubled;

        _geometry.CurrentSize = new Vector2(
            MathHelper.Clamp(windowSize.X, _geometry.MinimumSize.X, _geometry.MaximumSize.X),
            windowSize.Y);
    }

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
    /// bar sized to fit only whichever is bigger doesn't actually have room for both --
    /// confirmed by reproduction (a two-word notification title, "New Quest", with close/
    /// minimize buttons: the buttons, drawn on top per Window.Draw's ordering, covered the
    /// tail of the text instead of sitting past it).
    /// </summary>
    protected override float MinimumHeaderWidth()
    {
        var textSize = TitleFont.MeasureString(_titleText);
        return textSize.X + TitlePadding.X * 2 + TotalTitleButtonsWidth();
    }
}
