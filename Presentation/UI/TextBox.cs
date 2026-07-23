using Engine.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// An editable TextWindow -- typing appends, Backspace removes from the end. No
/// cursor-addressable editing, arrow-key navigation, click-to-position, selection, or
/// copy/paste yet -- see the Text Input Enhanced Features TODO for those. Enter submits
/// (TextSubmitted) and, if a sibling TextBox exists under the same parent, asks
/// GameInputController to hand focus to it. Shift+Enter inserts a newline instead, but only
/// when Multiline -- a single-line box treats Shift+Enter the same as a plain Enter.
/// </summary>
public sealed class TextBox(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer) : TextWindow(fontService, windowService, glyphRenderer)
{
    private static readonly Color FocusIndicatorColor = Color.Gold;
    private static readonly BorderThickness FocusIndicatorThickness = BorderThickness.Uniform(new Vector2(2, 2));

    /// <summary>Multiline boxes start tall enough for exactly this many lines and never shrink below it, regardless of how little text is in the box. See AutoSizeToContent.</summary>
    private const int MinimumVisibleLines = 2;

    private bool _multiline;

    /// <summary>Raised when Enter (or Shift+Enter on a non-multiline box) submits the current text.</summary>
    public event Action<string>? TextSubmitted;

    public override void BuildWindow(Window? parentWindow, WindowOptions windowOptions)
    {
        base.BuildWindow(parentWindow, windowOptions);

        _multiline = windowOptions.Text?.Multiline ?? false;
    }

    public override void Initialize()
    {
        base.Initialize();

        AutoSizeToContent();
    }

    protected override void OnTextInputAction(char character)
    {
        if (char.IsControl(character))
        {
            return;
        }

        SetTextAndAutoSize(OriginalText + character);
    }

    protected override void OnKeyPressAction(Keys key)
    {
        if (key != Keys.Back || OriginalText.Length == 0)
        {
            return;
        }

        SetTextAndAutoSize(OriginalText[..^1]);
    }

    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        if (!WasKeyPressed(keyboardState, previousKeyboardState, Keys.Enter))
        {
            return;
        }

        var shiftHeld = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
        if (shiftHeld && _multiline)
        {
            SetTextAndAutoSize(OriginalText + "\n");
        }
        else
        {
            TextSubmitted?.Invoke(OriginalText);

            var next = ParentWindow?.NextTextBoxAfter(this);
            if (next is not null)
            {
                RequestFocus(next);
            }
        }
    }

    private void SetTextAndAutoSize(string newText)
    {
        UpdateText(newText);
        AutoSizeToContent();
    }

    /// <summary>
    /// Grows/shrinks a multiline box's own window height, one line at a time, to exactly fit
    /// DisplayText.LineCount -- never below MinimumVisibleLines, capped by the window's own
    /// WindowMaximumSize (already enforced by RecalculateFixedWindowSize's existing min/max
    /// clamp; CanUserScrollVertical, set by whoever constructs a multiline TextBox, is what
    /// makes content beyond the cap still reachable). Deliberately not called from within
    /// ReformatDisplayText itself -- SetSize triggers a fresh MeasureAndArrange (and so a
    /// fresh ReformatDisplayText) of its own, which would make that call re-entrant. Called
    /// once after Initialize and again after every edit instead.
    /// </summary>
    private void AutoSizeToContent()
    {
        if (!_multiline)
        {
            return;
        }

        var desiredContentHeight = ContentFont.LineHeight * System.Math.Max(MinimumVisibleLines, DisplayText.LineCount) + LinePadding * 2;
        var desiredWindowHeight = desiredContentHeight + BorderInsetDoubled.Y + TitleInsetHeight;

        if (desiredWindowHeight != WindowCurrentSize.Y)
        {
            SetSize(new Vector2(WindowCurrentSize.X, desiredWindowHeight));

            // SetSize only re-measures this window itself. A WrapContent parent (e.g. the
            // quest-composer popup, sized to fit around this box) needs to be told separately
            // that a child it already contains has changed size -- AddChildWindow/
            // RemoveChildWindow handle that on attach/detach, but nothing does for a child
            // resizing itself afterward.
            ParentWindow?.MeasureAndArrange();
        }
    }

    /// <summary>
    /// StringUtility's word-wrap doesn't treat an embedded '\n' as a forced line break (see
    /// TextWindow.ReformatDisplayText's own doc comment) -- split on the newlines this box's
    /// Shift+Enter produces, wrap each newline-free segment independently (so the gap never
    /// comes into play), and rejoin, so a manual line break always lands exactly where it was
    /// typed regardless of how the rest of that segment wraps.
    /// </summary>
    public override void ReformatDisplayText()
    {
        if (!OriginalText.Contains('\n'))
        {
            base.ReformatDisplayText();
            return;
        }

        var measurer = new FontStashTextMeasurer(ContentFont);
        var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
        var segments = OriginalText.Split('\n');
        var wrappedSegments = new string[segments.Length];
        var totalLineCount = 0;

        for (var index = 0; index < segments.Length; index++)
        {
            var segmentDisplayText = StringUtility.FormatText(new FormatTextCriteria(measurer, maximumWidth, segments[index], FormatTextMode.Wordwrap));
            wrappedSegments[index] = segmentDisplayText.FormattedText;
            totalLineCount += System.Math.Max(1, segmentDisplayText.LineCount);
        }

        DisplayText = new DisplayText(OriginalText, string.Join('\n', wrappedSegments), totalLineCount);
    }

    public override void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        base.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);

        if (!IsFocused)
        {
            return;
        }

        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(WindowRectangle, FocusIndicatorThickness);
        spriteBatch.Draw(unitRectangle, top, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, bottom, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, left, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, right, FocusIndicatorColor);
    }
}