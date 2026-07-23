using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

public class TextWindow : Window
{
    public string OriginalText { get; set; } = string.Empty;
    public DisplayText DisplayText { get; set; }
    public SpriteFontBase ContentFont { get; set; }
    public Color TextColor { get; set; }
    protected const int LinePadding = 3;

    public TextWindow(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer)
        : base(fontService, windowService, glyphRenderer)
    {
        ContentFont = fontService.GetFont(8);
    }

    public override void BuildWindow(Window? parentWindow, WindowOptions windowOptions)
    {
        base.BuildWindow(parentWindow, windowOptions);

        OriginalText = windowOptions.Text?.Text ?? string.Empty;
        TextColor = windowOptions.Text?.TextColor ?? Color.Black;
        _canContainChildWindows = false;
    }

    public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!string.IsNullOrWhiteSpace(DisplayText.FormattedText))
        {
            var origin = RequiresContentViewport ? Vector2.Zero : ContentAbsolutePosition;
            spriteBatch.DrawString(ContentFont, DisplayText.FormattedText, origin + new Vector2(LinePadding, LinePadding), TextColor);
        }
    }

    protected override void RecalculateFixedWindowSize()
    {
        base.RecalculateFixedWindowSize();

        ReformatDisplayText();
        UpdateScrollBounds();
    }

    protected override void RecalculateFillWindowSize()
    {
        base.RecalculateFillWindowSize();

        ReformatDisplayText();
        UpdateScrollBounds();
    }

    protected override void RecalculateWrapContentWindowSize()
    {
        // Only a child window's MaximumSize is actually a parent-relative boundary (inherited
        // as _parentWindow.ContentSize, see BuildWindow) -- subtracting RelativePosition there
        // gives the space still available after this child's own offset within the parent. A
        // root window's MaximumSize (like a notification popup's explicit cap) is just a
        // literal bound with no parent edge to offset against; subtracting RelativePosition.X
        // from it used to pin the window's right edge to a fixed screen x regardless of
        // position -- invisible while notifications were stationary, but visible as "only the
        // right edge doesn't follow the drag" once Window Chrome Phase C made them draggable.
        // Y has the same shape as X here, so it gets the same ParentWindow-is-null check.
        // Clamped to 0, not left possibly negative: a tiled sibling positioned far enough down
        // a tall column (e.g. by earlier siblings' own long text) can have RelativePosition.Y
        // exceed MaximumSize.Y outright. An unclamped negative maximumContentHeight fed into
        // the Math.Min below would make this window's own Size.Y negative -- which then makes
        // RetileChildrenFrom's next-sibling chain (previousChildWindow.RelativePosition.Y +
        // previousChildWindow.CurrentSize.Y) step backward instead of forward, landing the next
        // sibling on top of this one instead of below it. Confirmed by reproduction: a
        // goblin-engineer-plus-dirt tile's dirt component windows overlapping each other, but
        // only once the goblin engineer's own (longer) text pushed dirt's block far enough down.
        var maximumContentWidth = System.Math.Max(0, ParentWindow is not null
            ? _geometry.MaximumSize.X - _geometry.RelativePosition.X - BorderInsetDoubled.X
            : _geometry.MaximumSize.X - BorderInsetDoubled.X);
        var maximumContentHeight = System.Math.Max(0, (ParentWindow is not null
            ? _geometry.MaximumSize.Y - _geometry.RelativePosition.Y
            : _geometry.MaximumSize.Y) - BorderInsetDoubled.Y - TitleInsetHeight);

        // Wrap against the maximum first (this is the width word-wrap decisions need), then
        // shrink the window itself to the widest line that wrapping actually produced --
        // StringUtility.FormatText already returns text unwrapped (i.e. at its own natural
        // width) whenever it fits within maximumContentWidth, so most short notifications
        // need far less than the full maximum and shouldn't claim it.
        _contentState.Size.X = maximumContentWidth;
        ReformatDisplayText();
        _contentState.Size.X = System.Math.Min(WidestLineWidth() + ContentPadding.X * 2, maximumContentWidth);

        if (_title.ShowTitle)
        {
            _contentState.Size.X = System.Math.Min(System.Math.Max(_contentState.Size.X, MinimumTitleWidth()), maximumContentWidth);
        }

        var wrappedTextHeight = TextContentHeight();

        // Clamped to maximumContentHeight, unlike width, which always has room to shrink into
        // (the window just gets narrower). Text with more lines than fit has no analogous
        // shrink-to-fit fallback, so without this clamp the window would grow past its own
        // MaximumSize instead of respecting it -- CanUserScrollVertical (see
        // NotificationCenter) is what makes the clamped-off remainder still reachable, via
        // UpdateScrollBounds below.
        _contentState.Size.Y = System.Math.Min(wrappedTextHeight, maximumContentHeight);

        _geometry.CurrentSize = _contentState.Size;
        if (_title.ShowTitle)
        {
            // Resize horizontally to fit the new content size, but keep the vertical size.
            _title.Size = new Vector2(_contentState.Size.X, _title.OriginalSize.Y - BorderInset.Y);
            _geometry.CurrentSize.Y += _title.Size.Y;
        }
        _geometry.CurrentSize += BorderInsetDoubled;

        UpdateScrollBounds();
    }

    private float WidestLineWidth()
    {
        if (string.IsNullOrEmpty(DisplayText.FormattedText))
        {
            return 0f;
        }

        var widest = 0f;
        foreach (var line in DisplayText.FormattedText.Split('\n'))
        {
            widest = Math.Max(widest, ContentFont.MeasureString(line.TrimEnd('\r')).X);
        }

        return widest;
    }

    private void UpdateScrollBounds()
    {
        var maxScrollY = System.Math.Max(0, TextContentHeight() - _contentState.Size.Y);

        // Word-wrap already keeps lines within the content width, but a single word too long
        // to break still overflows one line horizontally -- so this isn't always zero.
        var maxScrollX = System.Math.Max(0, WidestLineWidth() + ContentPadding.X * 2 - _contentState.Size.X);

        SetMaxScrollOffset(new Vector2(maxScrollX, maxScrollY));
    }

    /// <summary>Protected, not private -- TextBox uses this same formula (with an enforced minimum line count) to auto-size its own height to content. See TextBox.AutoSizeToContent.</summary>
    protected float TextContentHeight() => ContentFont.LineHeight * DisplayText.LineCount + LinePadding * 2;

    /// <summary>
    /// Virtual so TextBox can override it: StringUtility's word-wrap chunks/splits on spaces
    /// only and doesn't treat an embedded '\n' as a forced line break (confirmed by
    /// StringUtilityTests.SimpleWordWrap_EmbeddedNewlineNotAtChunkBoundary...) -- fine for
    /// this base class (nothing here ever produces embedded newlines), but a TextBox's own
    /// Shift+Enter does, so it needs to wrap around that gap instead of assuming it away.
    /// </summary>
    public virtual void ReformatDisplayText()
    {
        DisplayText = StringUtility.FormatText(new FormatTextCriteria(
            new FontStashTextMeasurer(ContentFont),
            _contentState.Size.X - ContentPadding.X * 2,
            OriginalText,
            FormatTextMode.Wordwrap));
    }

    public void UpdateText(string newText)
    {
        OriginalText = newText;

        switch (_geometry.DisplayMode)
        {
            case WindowDisplayMode.Fixed:
                RecalculateFixedWindowSize();
                break;
            case WindowDisplayMode.Fill:
                RecalculateFillWindowSize();
                break;
            case WindowDisplayMode.WrapContent:
                RecalculateWrapContentWindowSize();
                break;
        }

        // Without this, a text-driven resize left the window's rectangles/absolute
        // positions/title button positions stale (still reflecting the size before the
        // text change) even though CurrentSize/ContentSize above were already correct.
        Arrange();
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        // TODO copy text to clipboard
    }
}
