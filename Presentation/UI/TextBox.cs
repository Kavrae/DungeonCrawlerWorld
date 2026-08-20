using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;
using SDL3;
using System.Text;

namespace Presentation.UI;

/// <summary>
/// An editable TextWindow with cursor-addressable editing (insert/delete at an arbitrary
/// caret position, not just the end), full selection support (click-to-position,
/// double/triple-click word/line select, Shift+click, Shift+arrow/Home/End/Ctrl+Home/End,
/// click-drag via UiInputController, Ctrl+A), Ctrl+word-deletion, key-repeat on held
/// Backspace/Delete/arrows, typing/Backspace/Delete replacing an active selection, and
/// Ctrl+C/Ctrl+X/Ctrl+V clipboard support (via FNA.NET's own SDL3 clipboard wrapper -- no
/// new dependency) -- see the Text Input Enhanced Features TODO. Enter submits
/// (TextSubmitted) and, if a sibling TextBox exists under the same parent, asks
/// UiInputController to hand focus to it. Shift+Enter inserts a newline at the caret
/// instead (replacing any active selection first), but only when Multiline -- a
/// single-line box treats Shift+Enter the same as a plain Enter.
/// </summary>
public sealed class TextBox(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer, CursorTextContent? cursorTextContent = null) : TextWindow(fontService, elementPoolService, glyphRenderer)
{
    private static readonly Color FocusIndicatorColor = Color.Gold;
    private static readonly BorderThickness FocusIndicatorThickness = BorderThickness.Uniform(new Vector2(2, 2));

    /// <summary>Multiline boxes start tall enough for exactly this many lines and never shrink below it, regardless of how little text is in the box. See AutoSizeToContent.</summary>
    private const int MinimumVisibleLines = 2;

    /// <summary>Same delay-gated frame-counter idiom as HudMetrics.HoverTooltipDelayFrames/DebouncedTextFilter -- toggles CaretVisible every half-second while focused.</summary>
    private static readonly int CaretBlinkIntervalFrames = GameTiming.FramesForSeconds(0.5f);

    /// <summary>Roughly standard OS double-click timing -- a second click on (near) the same character within this window counts as a double-click; a third, a triple-click. See RegisterClickForMultiClick.</summary>
    private static readonly int MultiClickWindowFrames = GameTiming.FramesForSeconds(0.4f);

    /// <summary>How long a repeatable key (Backspace/Delete/Left/Right/Up/Down) must be held before it starts auto-repeating -- see ShouldFire.</summary>
    private static readonly int KeyRepeatInitialDelayFrames = GameTiming.FramesForSeconds(0.4f);

    /// <summary>How often a held repeatable key re-fires once past the initial delay -- see ShouldFire.</summary>
    private static readonly int KeyRepeatIntervalFrames = GameTiming.FramesForSeconds(0.05f);

    private static readonly Color SelectionColor = Color.CornflowerBlue * 0.4f;

    private const int CaretWidth = 2;

    private bool _multiline;

    /// <summary>
    /// Visual-line spans over OriginalText (Start/Length are OriginalText indices, not
    /// FormattedText indices) -- the caret/selection/click-hit-testing infrastructure the Text
    /// Input Enhanced Features TODO calls for needs an index-aligned mapping from a visual line
    /// back to OriginalText, which TextWindow's inherited hyphenating wrap can't provide (it
    /// inserts hyphen characters and '\n' breaks that don't exist in OriginalText). A real text
    /// *input* field shouldn't hyphenate what's actively being edited anyway -- hyphenation
    /// belongs to read-only body text (TextWindow), not this class. See ReformatDisplayText.
    /// </summary>
    private readonly List<(int Start, int Length)> _lineSpans = [];

    /// <summary>OriginalText index the caret currently sits at, 0..OriginalText.Length. See MoveCaretTo.</summary>
    private int _caretIndex;

    /// <summary>
    /// Single-line only: the OriginalText index of the first character currently drawn --
    /// advances/retreats in EnsureCaretVisible to keep the caret within the box's own width,
    /// the same "scroll to keep the caret visible" behavior every standard single-line text
    /// input has. Multiline doesn't need this -- word-wrap already keeps every line within the
    /// content width, and it scrolls vertically instead via the inherited CanUserScrollVertical.
    /// </summary>
    private int _visibleStartIndex;

    /// <summary>
    /// The horizontal pixel position Up/Down should aim for, preserved across consecutive
    /// vertical moves so stepping through shorter lines doesn't permanently snap the column back
    /// -- standard editor behavior. Null means "derive it fresh from the current caret position,"
    /// which every non-vertical caret move resets it to (see MoveCaretTo).
    /// </summary>
    private float? _desiredCaretPixelX;

    private int _caretBlinkFrames;
    private bool _caretVisible = true;

    /// <summary>
    /// The other end of the current selection, if any -- OriginalText index. The selection range
    /// is always [min(anchor, _caretIndex), max(anchor, _caretIndex)). Null means no selection.
    /// MoveCaretTo always clears this; any operation that wants to create or extend a selection
    /// (double/triple-click, Shift+click) re-sets it immediately after calling MoveCaretTo, so a
    /// plain caret move (typing, a bare arrow key, a plain click) always collapses whatever
    /// selection existed -- standard behavior in every text field.
    /// </summary>
    private int? _selectionAnchor;

    /// <summary>Per-key held-frame counters driving ShouldFire's repeat timing -- see its own doc comment.</summary>
    private readonly Dictionary<Keys, int> _heldFramesByRepeatableKey = new();

    private int _framesSinceLastClick = int.MaxValue;
    private int _consecutiveClickCount;
    private int _lastClickIndex = -1;

    /// <summary>Raised when Enter (or Shift+Enter on a non-multiline box) submits the current text.</summary>
    public event Action<string>? TextSubmitted;

    /// <summary>Placeholder text shown whenever the box is both empty and unfocused (see DrawContent) -- null (the default) draws nothing, so every existing TextBox usage is unaffected.</summary>
    public string? GhostText { get; set; }

    public Color GhostTextColor { get; set; } = Color.LightGray;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _multiline = options.Text?.Multiline ?? false;

        // TextBox instances are pooled and reused (e.g. the Quest Composer's box on every "New
        // Quest" click) -- Build is where OriginalText itself already resets for a fresh use, so
        // every bit of caret state derived from a previous use needs to reset here too, or a
        // reopened box would start with a stale caret position from whatever it last held.
        _caretIndex = 0;
        _visibleStartIndex = 0;
        _desiredCaretPixelX = null;
        _caretBlinkFrames = 0;
        _caretVisible = true;
        _selectionAnchor = null;
        _heldFramesByRepeatableKey.Clear();
        _framesSinceLastClick = int.MaxValue;
        _consecutiveClickCount = 0;
        _lastClickIndex = -1;
        GhostText = null;
        GhostTextColor = Color.LightGray;
    }

    public override void Initialize()
    {
        base.Initialize();

        AutoSizeToContent();
    }

    /// <summary>Blinks the caret while focused (regaining focus always starts from a solid, visible caret rather than resuming mid-blink), and ages out the double/triple-click window -- see RegisterClickForMultiClick.</summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_framesSinceLastClick < int.MaxValue)
        {
            _framesSinceLastClick++;
        }

        if (!IsFocused)
        {
            _caretBlinkFrames = 0;
            _caretVisible = true;
            return;
        }

        _caretBlinkFrames++;
        if (_caretBlinkFrames < CaretBlinkIntervalFrames)
        {
            return;
        }

        _caretBlinkFrames = 0;
        _caretVisible = !_caretVisible;
    }

    protected override void OnTextInputAction(char character)
    {
        if (char.IsControl(character))
        {
            return;
        }

        TryDeleteSelection();

        var insertIndex = _caretIndex;
        SetTextAndAutoSize(OriginalText[..insertIndex] + character + OriginalText[insertIndex..]);
        MoveCaretTo(insertIndex + 1);
    }

    protected override void OnHotkeysAction(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        var ctrlHeld = keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);
        var shiftHeld = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Enter))
        {
            if (shiftHeld && _multiline)
            {
                TryDeleteSelection();
                var insertIndex = _caretIndex;
                SetTextAndAutoSize(OriginalText[..insertIndex] + "\n" + OriginalText[insertIndex..]);
                MoveCaretTo(insertIndex + 1);
            }
            else
            {
                TextSubmitted?.Invoke(OriginalText);

                var next = ParentElement?.NextFocusableDescendant(this);
                if (next is not null)
                {
                    RequestFocus(next);
                }
            }

            return;
        }

        if (ctrlHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.A))
        {
            MoveCaretTo(OriginalText.Length);
            _selectionAnchor = 0;
            return;
        }

        if (ctrlHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.C))
        {
            CopySelectionToClipboard();
            return;
        }

        if (ctrlHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.X))
        {
            if (CopySelectionToClipboard())
            {
                TryDeleteSelection();
            }
            return;
        }

        if (ctrlHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.V))
        {
            PasteFromClipboard();
            return;
        }

        // Backspace/Delete/Left/Right/Up/Down all repeat while held (ShouldFire), instead of the
        // edit-hook's own edge-triggered HandleKeyPress -- see ShouldFire's own doc comment for
        // why Backspace/Delete moved here alongside the arrows rather than staying split across
        // two different input hooks with two different repeat behaviors.
        if (ShouldFire(Keys.Back, keyboardState) && !TryDeleteSelection())
        {
            if (ctrlHeld && _caretIndex > 0)
            {
                var wordStart = FindPreviousWordBoundary(_caretIndex);
                SetTextAndAutoSize(OriginalText[..wordStart] + OriginalText[_caretIndex..]);
                MoveCaretTo(wordStart);
            }
            else if (_caretIndex > 0)
            {
                var backspaceIndex = _caretIndex - 1;
                SetTextAndAutoSize(OriginalText[..backspaceIndex] + OriginalText[_caretIndex..]);
                MoveCaretTo(backspaceIndex);
            }
        }

        if (ShouldFire(Keys.Delete, keyboardState) && !TryDeleteSelection())
        {
            if (ctrlHeld && _caretIndex < OriginalText.Length)
            {
                var wordEnd = FindNextWordBoundary(_caretIndex);
                SetTextAndAutoSize(OriginalText[.._caretIndex] + OriginalText[wordEnd..]);
                MoveCaretTo(_caretIndex);
            }
            else if (_caretIndex < OriginalText.Length)
            {
                SetTextAndAutoSize(OriginalText[.._caretIndex] + OriginalText[(_caretIndex + 1)..]);
                MoveCaretTo(_caretIndex);
            }
        }

        if (ShouldFire(Keys.Left, keyboardState))
        {
            MoveCaretPossiblyExtendingSelection(ctrlHeld ? FindPreviousWordBoundary(_caretIndex) : System.Math.Max(0, _caretIndex - 1), shiftHeld);
        }

        if (ShouldFire(Keys.Right, keyboardState))
        {
            MoveCaretPossiblyExtendingSelection(ctrlHeld ? FindNextWordBoundary(_caretIndex) : System.Math.Min(OriginalText.Length, _caretIndex + 1), shiftHeld);
        }

        if (_multiline && ShouldFire(Keys.Up, keyboardState))
        {
            MoveCaretPossiblyExtendingSelection(ComputeVerticalCaretMove(-1), shiftHeld, preserveDesiredPixelX: true);
        }

        if (_multiline && ShouldFire(Keys.Down, keyboardState))
        {
            MoveCaretPossiblyExtendingSelection(ComputeVerticalCaretMove(1), shiftHeld, preserveDesiredPixelX: true);
        }

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.Home))
        {
            MoveCaretPossiblyExtendingSelection(ctrlHeld ? 0 : CurrentLineStart(), shiftHeld);
        }

        if (WasKeyPressed(keyboardState, previousKeyboardState, Keys.End))
        {
            MoveCaretPossiblyExtendingSelection(ctrlHeld ? OriginalText.Length : CurrentLineEnd(), shiftHeld);
        }
    }

    /// <summary>
    /// The shared shape behind every Shift+navigation combo (Shift+Left/Right/Up/Down/Home/End,
    /// Shift+Ctrl+Home/End): moves the caret exactly like the plain key would, but if Shift is
    /// held, first captures the current selection's far end (or the caret itself if there's no
    /// selection yet) and restores it as the anchor afterward -- MoveCaretTo always clears
    /// _selectionAnchor, see its own doc comment. Without Shift this is just MoveCaretTo.
    /// </summary>
    private void MoveCaretPossiblyExtendingSelection(int newIndex, bool shiftHeld, bool preserveDesiredPixelX = false)
    {
        if (!shiftHeld)
        {
            MoveCaretTo(newIndex, preserveDesiredPixelX);
            return;
        }

        var anchor = _selectionAnchor ?? _caretIndex;
        MoveCaretTo(newIndex, preserveDesiredPixelX);
        _selectionAnchor = anchor;
    }

    /// <summary>Deletes the current selection (if any) and collapses the caret to where it started -- the shared "typing/Backspace/Delete/Shift+Enter replaces an active selection" behavior every text field has. Returns false (no-op) when there's no selection, so callers can fall through to their own single-character/word behavior.</summary>
    private bool TryDeleteSelection()
    {
        if (_selectionAnchor is not { } anchor || anchor == _caretIndex)
        {
            return false;
        }

        var selectionStart = System.Math.Min(anchor, _caretIndex);
        var selectionEnd = System.Math.Max(anchor, _caretIndex);
        SetTextAndAutoSize(OriginalText[..selectionStart] + OriginalText[selectionEnd..]);
        MoveCaretTo(selectionStart);
        return true;
    }

    /// <summary>
    /// Copies the current selection to the OS clipboard via FNA.NET's own SDL3 wrapper (no new
    /// dependency -- SDL3.SDL.SDL_SetClipboardText is already public, the same native layer
    /// TextInputEXT/MouseCursorEXT wrap) and shows a brief "Copied" confirmation near the cursor
    /// (CursorTextContent) -- a silent clipboard write would otherwise give no feedback at all
    /// that anything happened. No-op (including no toast) when nothing is selected, matching how
    /// most plain-text fields treat Ctrl+C with no selection -- not "copy the whole field."
    /// Returns whether anything was actually copied, so Ctrl+X can gate its own delete on it.
    /// </summary>
    private bool CopySelectionToClipboard()
    {
        if (_selectionAnchor is not { } anchor || anchor == _caretIndex)
        {
            return false;
        }

        var selectionStart = System.Math.Min(anchor, _caretIndex);
        var selectionEnd = System.Math.Max(anchor, _caretIndex);
        SDL.SDL_SetClipboardText(OriginalText[selectionStart..selectionEnd]);
        cursorTextContent?.Show("Copied");
        return true;
    }

    /// <summary>
    /// Inserts the OS clipboard's text at the caret, replacing the current selection first if
    /// any. Filters the same way OnTextInputAction filters typed characters (char.IsControl), plus
    /// '\r' is always dropped (Windows clipboard text carries CRLF line endings, and nothing in
    /// this codebase's font rendering has a glyph for a bare carriage return -- see
    /// StringUtility.LineBreak's own doc comment for the same issue elsewhere) and '\n' is kept
    /// only when Multiline, dropped otherwise -- a single-line box should never end up with an
    /// embedded newline no matter what's on the clipboard.
    /// </summary>
    private void PasteFromClipboard()
    {
        var clipboardText = SDL.SDL_GetClipboardText();
        if (string.IsNullOrEmpty(clipboardText))
        {
            return;
        }

        var filtered = new StringBuilder(clipboardText.Length);
        foreach (var character in clipboardText)
        {
            if (character == '\r')
            {
                continue;
            }
            if (character == '\n')
            {
                if (_multiline)
                {
                    filtered.Append(character);
                }
                continue;
            }
            if (!char.IsControl(character))
            {
                filtered.Append(character);
            }
        }

        if (filtered.Length == 0)
        {
            return;
        }

        TryDeleteSelection();

        var insertIndex = _caretIndex;
        var insertedText = filtered.ToString();
        SetTextAndAutoSize(OriginalText[..insertIndex] + insertedText + OriginalText[insertIndex..]);
        MoveCaretTo(insertIndex + insertedText.Length);
    }

    /// <summary>
    /// True the frame a repeatable key is first pressed, then again after
    /// KeyRepeatInitialDelayFrames, then every KeyRepeatIntervalFrames while still held -- the
    /// standard press-then-repeat shape every OS text field uses, applied uniformly to
    /// Backspace/Delete/Left/Right/Up/Down (the TODO's key-repeat item only calls out
    /// Backspace/Delete explicitly, but every OS text field also repeats held arrow-key
    /// navigation, so building one shared mechanism now avoids redoing this the first time
    /// arrow-hold is noticed not to repeat). Always called once per repeatable key per frame
    /// (even when its own action wouldn't fire, e.g. Backspace at index 0) so held-frame
    /// tracking stays accurate regardless of what the caller does with the result.
    /// </summary>
    private bool ShouldFire(Keys key, KeyboardState keyboardState)
    {
        if (!keyboardState.IsKeyDown(key))
        {
            _heldFramesByRepeatableKey.Remove(key);
            return false;
        }

        if (!_heldFramesByRepeatableKey.TryGetValue(key, out var heldFrames))
        {
            _heldFramesByRepeatableKey[key] = 0;
            return true;
        }

        heldFrames++;
        _heldFramesByRepeatableKey[key] = heldFrames;

        if (heldFrames < KeyRepeatInitialDelayFrames)
        {
            return false;
        }

        return (heldFrames - KeyRepeatInitialDelayFrames) % KeyRepeatIntervalFrames == 0;
    }

    /// <summary>
    /// Click-to-position-cursor, double-click-selects-word, triple-click-selects-line, and
    /// Shift+click-extends-selection, all sharing the same hit-tested index. Shift+click never
    /// counts toward the double/triple-click sequence -- matches standard OS behavior, where
    /// shift-click is always a plain extend regardless of how many plain clicks preceded it.
    /// </summary>
    protected override void OnContentClickAction(Point mousePosition)
    {
        var clickedIndex = HitTestCaretIndex(mousePosition);
        var keyboardState = Keyboard.GetState();
        var shiftHeld = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);

        if (shiftHeld)
        {
            _consecutiveClickCount = 0;
            var anchor = _selectionAnchor ?? _caretIndex;
            MoveCaretTo(clickedIndex);
            _selectionAnchor = anchor;
            return;
        }

        switch (RegisterClickForMultiClick(clickedIndex))
        {
            case 2:
                SelectWord(clickedIndex);
                break;
            case 3:
                SelectLine(clickedIndex);
                break;
            default:
                MoveCaretTo(clickedIndex);
                break;
        }
    }

    /// <summary>
    /// Maps an absolute screen-space click position (see Element.OnContentClickAction's own
    /// contract) to an OriginalText index. Multiline reads in local (Viewport-relative)
    /// coordinates -- it's drawn through RequiresContentViewport's CameraTransform, which
    /// translates by -ScrollOffset, so a click needs the same offset added back to land in the
    /// same coordinate space _lineSpans was measured in. Single-line has no such transform (see
    /// the class doc comment on why it deliberately avoids RequiresContentViewport) and measures
    /// from _visibleStartIndex instead, since that's where drawing actually starts.
    /// </summary>
    private int HitTestCaretIndex(Point mousePosition)
    {
        if (_multiline)
        {
            var localX = mousePosition.X - ContentAbsolutePosition.X + ScrollOffset.X - LinePadding;
            var localY = mousePosition.Y - ContentAbsolutePosition.Y + ScrollOffset.Y - LinePadding;
            var lineIndex = System.Math.Clamp((int)(localY / ContentFont.LineHeight), 0, _lineSpans.Count - 1);
            var (lineStart, lineLength) = _lineSpans[lineIndex];
            return lineStart + FindColumnForPixelX(lineStart, lineLength, localX);
        }

        var visibleX = mousePosition.X - ContentAbsolutePosition.X - LinePadding;
        return _visibleStartIndex + FindColumnForPixelX(_visibleStartIndex, OriginalText.Length - _visibleStartIndex, visibleX);
    }

    /// <summary>
    /// Tracks consecutive clicks landing on (near) the same character within MultiClickWindowFrames
    /// of each other -- returns 1 for a plain click, 2 for a double-click, capped at 3 for a
    /// triple-click (and every further rapid click at the same spot, rather than cycling back to
    /// 1). A 1-character tolerance stands in for pixel proximity: a real second click on the same
    /// intended spot almost always resolves to the same or an adjacent character.
    /// </summary>
    private int RegisterClickForMultiClick(int clickedIndex)
    {
        if (_framesSinceLastClick <= MultiClickWindowFrames && System.Math.Abs(clickedIndex - _lastClickIndex) <= 1)
        {
            _consecutiveClickCount = System.Math.Min(3, _consecutiveClickCount + 1);
        }
        else
        {
            _consecutiveClickCount = 1;
        }

        _framesSinceLastClick = 0;
        _lastClickIndex = clickedIndex;
        return _consecutiveClickCount;
    }

    /// <summary>Selects the contiguous non-whitespace run containing clickedIndex -- FindPreviousWordBoundary/FindNextWordBoundary already resolve to exactly that word's own start/end when clickedIndex sits inside it (their whitespace-skip loops no-op immediately). A click that lands exactly on whitespace instead spans outward into both neighboring words -- an acceptable rough edge, not worth a special case for now.</summary>
    private void SelectWord(int clickedIndex)
    {
        var wordStart = FindPreviousWordBoundary(clickedIndex);
        var wordEnd = FindNextWordBoundary(clickedIndex);
        MoveCaretTo(wordEnd);
        _selectionAnchor = wordStart;
    }

    private void SelectLine(int clickedIndex)
    {
        var (start, length) = _lineSpans[FindLineIndexFor(clickedIndex)];
        MoveCaretTo(start + length);
        _selectionAnchor = start;
    }

    /// <summary>
    /// Starts a click-drag text selection at the press position -- called by UiInputController
    /// once a left-button press on this box's content has moved past its own drag tap threshold
    /// (see HandleTextSelectionDrag). Caret and anchor both start at the same hit-tested
    /// position, the same "zero-length selection to begin with" shape a plain click already
    /// produces -- ExtendSelectionDrag is what actually grows it from here.
    /// </summary>
    internal void BeginSelectionDrag(Point pressPosition)
    {
        var index = HitTestCaretIndex(pressPosition);
        MoveCaretTo(index);
        _selectionAnchor = index;
    }

    /// <summary>Extends the selection anchored by BeginSelectionDrag to currentPosition -- called every frame the drag continues, the click-drag equivalent of Shift+click's own anchor-capture/MoveCaretTo/anchor-restore shape.</summary>
    internal void ExtendSelectionDrag(Point currentPosition)
    {
        var anchor = _selectionAnchor ?? _caretIndex;
        MoveCaretTo(HitTestCaretIndex(currentPosition));
        _selectionAnchor = anchor;
    }

    /// <summary>
    /// Moves the caret, clamping to OriginalText's bounds, and resets the blink timer to a
    /// solid, visible caret -- otherwise it could sit invisible for up to half the blink interval
    /// right after an edit or a navigation key, reading as unresponsive. preserveDesiredPixelX is
    /// true only for the Up/Down callers immediately below (see _desiredCaretPixelX's own doc
    /// comment); every other caller re-derives it fresh from wherever the caret actually lands.
    /// Always clears _selectionAnchor -- see its own doc comment for why every selection-creating
    /// caller re-sets it immediately afterward instead of this method taking an opt-out flag.
    /// </summary>
    private void MoveCaretTo(int newIndex, bool preserveDesiredPixelX = false)
    {
        _caretIndex = System.Math.Clamp(newIndex, 0, OriginalText.Length);
        _selectionAnchor = null;

        if (!preserveDesiredPixelX)
        {
            _desiredCaretPixelX = null;
        }

        _caretBlinkFrames = 0;
        _caretVisible = true;

        if (_multiline)
        {
            EnsureCaretVisibleMultiline();
        }
        else
        {
            EnsureCaretVisible();
        }
    }

    /// <summary>
    /// Scrolls _visibleStartIndex just far enough to keep the caret on-screen -- left if the
    /// caret moved before the current window, right (via binary search for the smallest fitting
    /// start index, since width shrinks monotonically as _visibleStartIndex advances toward
    /// _caretIndex) if the substring from the window's start to the caret no longer fits.
    /// Single-line only; see _visibleStartIndex's own doc comment.
    /// </summary>
    private void EnsureCaretVisible()
    {
        _visibleStartIndex = System.Math.Clamp(_visibleStartIndex, 0, OriginalText.Length);

        if (_caretIndex < _visibleStartIndex)
        {
            _visibleStartIndex = _caretIndex;
            return;
        }

        var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
        if (ContentFont.MeasureString(OriginalText[_visibleStartIndex.._caretIndex]).X <= maximumWidth)
        {
            return;
        }

        var low = _visibleStartIndex;
        var high = _caretIndex;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (ContentFont.MeasureString(OriginalText[mid.._caretIndex]).X > maximumWidth)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        _visibleStartIndex = low;
    }

    /// <summary>Multiline analogue of EnsureCaretVisible -- scrolls vertically (ScrollBy) just far enough to keep the caret's own visual line within the currently visible content height.</summary>
    private void EnsureCaretVisibleMultiline()
    {
        var lineIndex = FindLineIndexFor(_caretIndex);
        var caretTop = ContentFont.LineHeight * lineIndex;
        var caretBottom = caretTop + ContentFont.LineHeight;

        if (caretTop < ScrollOffset.Y)
        {
            ScrollBy(new Vector2(0, caretTop - ScrollOffset.Y));
        }
        else if (caretBottom > ScrollOffset.Y + _contentState.Size.Y)
        {
            ScrollBy(new Vector2(0, caretBottom - _contentState.Size.Y - ScrollOffset.Y));
        }
    }

    /// <summary>Index into _lineSpans of the visual line containing index. A position sitting exactly on a wrap boundary resolves to the earlier line (its own end) rather than the next line's start -- an arbitrary but consistent choice, since nothing currently distinguishes the two positions visually (no hyphen/break glyph is ever inserted, see ReformatDisplayText).</summary>
    private int FindLineIndexFor(int index)
    {
        for (var lineIndex = 0; lineIndex < _lineSpans.Count - 1; lineIndex++)
        {
            var (start, length) = _lineSpans[lineIndex];
            if (index <= start + length)
            {
                return lineIndex;
            }
        }

        return _lineSpans.Count - 1;
    }

    private int CurrentLineStart() => _lineSpans[FindLineIndexFor(_caretIndex)].Start;

    private int CurrentLineEnd()
    {
        var (start, length) = _lineSpans[FindLineIndexFor(_caretIndex)];
        return start + length;
    }

    /// <summary>
    /// Where the caret should land after an Up/Down press: the same desired pixel X (see its own
    /// doc comment) mapped onto the target line via FindColumnForPixelX. Returns the current
    /// caret position unchanged if already on the first/last line -- Up/Down doesn't wrap or hand
    /// off to anything else.
    /// </summary>
    private int ComputeVerticalCaretMove(int lineDelta)
    {
        var lineIndex = FindLineIndexFor(_caretIndex);
        var targetLineIndex = System.Math.Clamp(lineIndex + lineDelta, 0, _lineSpans.Count - 1);

        if (targetLineIndex == lineIndex)
        {
            return _caretIndex;
        }

        var desiredX = _desiredCaretPixelX ?? CaretPixelXWithinLine(lineIndex, _caretIndex);
        _desiredCaretPixelX = desiredX;

        var (targetStart, targetLength) = _lineSpans[targetLineIndex];
        return targetStart + FindColumnForPixelX(targetStart, targetLength, desiredX);
    }

    private float CaretPixelXWithinLine(int lineIndex, int caretIndex)
    {
        var lineStart = _lineSpans[lineIndex].Start;
        return ContentFont.MeasureString(OriginalText[lineStart..caretIndex]).X;
    }

    /// <summary>
    /// The furthest column (0..lineLength) within [lineStart, lineStart + lineLength) whose pixel
    /// width is still &lt;= targetX -- the inverse of CaretPixelXWithinLine, used to land Up/Down
    /// on the nearest character to a desired horizontal position. Binary search rather than a
    /// linear scan -- called on every click/Up/Down, and width is monotonically non-decreasing as
    /// the substring grows, the same assumption a linear break-early scan already relied on.
    /// </summary>
    private int FindColumnForPixelX(int lineStart, int lineLength, float targetX)
    {
        var low = 0;
        var high = lineLength;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (ContentFont.MeasureString(OriginalText[lineStart..(lineStart + mid)]).X > targetX)
            {
                high = mid - 1;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    /// <summary>Skips any whitespace immediately before fromIndex, then the contiguous non-whitespace run before that -- the standard Ctrl+Left word-jump shape, also shared by Ctrl+Backspace and double-click word-select once those land.</summary>
    private int FindPreviousWordBoundary(int fromIndex)
    {
        var index = fromIndex;

        while (index > 0 && char.IsWhiteSpace(OriginalText[index - 1]))
        {
            index--;
        }
        while (index > 0 && !char.IsWhiteSpace(OriginalText[index - 1]))
        {
            index--;
        }

        return index;
    }

    /// <summary>Mirrors FindPreviousWordBoundary forward -- skips whitespace at fromIndex, then the contiguous non-whitespace run after that.</summary>
    private int FindNextWordBoundary(int fromIndex)
    {
        var index = fromIndex;
        var length = OriginalText.Length;

        while (index < length && char.IsWhiteSpace(OriginalText[index]))
        {
            index++;
        }
        while (index < length && !char.IsWhiteSpace(OriginalText[index]))
        {
            index++;
        }

        return index;
    }

    private void SetTextAndAutoSize(string newText)
    {
        UpdateText(newText);
        AutoSizeToContent();
    }

    /// <summary>
    /// Ghost text hides the moment the box has real content OR gains focus (not just once
    /// something's typed) -- e.g. Inventory tab search's box: click it and the placeholder is
    /// gone immediately, even before the first keystroke. A single-line box with overflowing real
    /// text draws only the window starting at _visibleStartIndex that fits (GetVisibleWindowText)
    /// instead of the full line, so it clips at its own left edge and keeps scrolling to follow
    /// the caret -- matching standard text-input behavior. The caret itself (DrawCaretIfFocused)
    /// draws last so it's never hidden behind either branch's text.
    /// </summary>
    /// <remarks>
    /// Deliberately doesn't use the shared scrollable-window Viewport/scissor clipping
    /// (Element.Draw's RequiresContentViewport path, driven by CanUserScrollHorizontal/Vertical)
    /// for the single-line case: that mechanism nests a second spriteBatch End/Begin pair mid-draw,
    /// which corrupts an already-active outer End/Begin pair when this TextBox is itself drawn
    /// nested inside an ancestor window that's also scrollable (confirmed live: a search box
    /// nested inside a scrollable Inventory window turned a sibling element's background white
    /// while the search box was focused). Multiline still opts into that machinery via
    /// CanUserScrollVertical (word-wrap already keeps every line within the content width, so
    /// there's nothing horizontal left to clip) -- only the single-line windowed-substring path
    /// here is new.
    /// </remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (string.IsNullOrEmpty(OriginalText) && !IsFocused && !string.IsNullOrEmpty(GhostText))
        {
            // Multiline still uses the local-coordinate Viewport (RequiresContentViewport, via
            // CanUserScrollVertical) TextWindow's own DrawContent relies on -- no current
            // multiline TextBox sets GhostText, but this keeps the origin correct if one ever
            // does, rather than assuming every ghost-text box is single-line. No caret to draw
            // here either way -- ghost text only shows while unfocused.
            var origin = RequiresContentViewport ? Vector2.Zero : ContentAbsolutePosition;
            spriteBatch.DrawString(ContentFont, GhostText, origin + new Vector2(LinePadding, LinePadding), GhostTextColor);
            return;
        }

        DrawSelectionIfAny(spriteBatch, unitRectangle);

        if (_multiline || string.IsNullOrEmpty(DisplayText.FormattedText))
        {
            base.DrawContent(gameTime);
        }
        else
        {
            var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
            var visibleText = GetVisibleWindowText(DisplayText.FormattedText, _visibleStartIndex, maximumWidth);
            spriteBatch.DrawString(ContentFont, visibleText, ContentAbsolutePosition + new Vector2(LinePadding, LinePadding), TextColor);
        }

        DrawCaretIfFocused(spriteBatch, unitRectangle);
    }

    /// <summary>Draws a translucent highlight behind the current selection, if any -- called before the text itself so the highlight sits behind the glyphs, not on top of them.</summary>
    private void DrawSelectionIfAny(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_selectionAnchor is not { } anchor || anchor == _caretIndex)
        {
            return;
        }

        var selectionStart = System.Math.Min(anchor, _caretIndex);
        var selectionEnd = System.Math.Max(anchor, _caretIndex);

        if (_multiline)
        {
            DrawMultilineSelection(spriteBatch, unitRectangle, selectionStart, selectionEnd);
        }
        else
        {
            DrawSingleLineSelection(spriteBatch, unitRectangle, selectionStart, selectionEnd);
        }
    }

    private void DrawSingleLineSelection(SpriteBatch spriteBatch, Texture2D unitRectangle, int selectionStart, int selectionEnd)
    {
        var visibleSelectionStart = System.Math.Max(selectionStart, _visibleStartIndex);
        if (visibleSelectionStart >= selectionEnd)
        {
            return;
        }

        var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
        var startX = System.Math.Min(ContentFont.MeasureString(OriginalText[_visibleStartIndex..visibleSelectionStart]).X, maximumWidth);
        var endX = System.Math.Min(ContentFont.MeasureString(OriginalText[_visibleStartIndex..selectionEnd]).X, maximumWidth);

        if (endX <= startX)
        {
            return;
        }

        var position = ContentAbsolutePosition + new Vector2(LinePadding + startX, LinePadding);
        var rectangle = new Rectangle((int)position.X, (int)position.Y, (int)(endX - startX), (int)ContentFont.LineHeight);
        spriteBatch.Draw(unitRectangle, rectangle, SelectionColor);
    }

    /// <summary>One highlight rectangle per spanned visual line -- the first line from selectionStart to its own end, middle lines full width, the last line from its own start to selectionEnd. Drawn in the same local (Viewport-relative) coordinates as the multiline text/caret, since this runs inside the same RequiresContentViewport Begin/End pair.</summary>
    private void DrawMultilineSelection(SpriteBatch spriteBatch, Texture2D unitRectangle, int selectionStart, int selectionEnd)
    {
        var startLine = FindLineIndexFor(selectionStart);
        var endLine = FindLineIndexFor(selectionEnd);

        for (var lineIndex = startLine; lineIndex <= endLine; lineIndex++)
        {
            var (lineStart, lineLength) = _lineSpans[lineIndex];
            var rangeStart = lineIndex == startLine ? selectionStart : lineStart;
            var rangeEnd = lineIndex == endLine ? selectionEnd : lineStart + lineLength;

            if (rangeEnd <= rangeStart)
            {
                continue;
            }

            var startX = ContentFont.MeasureString(OriginalText[lineStart..rangeStart]).X;
            var endX = ContentFont.MeasureString(OriginalText[lineStart..rangeEnd]).X;
            var y = ContentFont.LineHeight * lineIndex;

            var rectangle = new Rectangle((int)(LinePadding + startX), (int)(LinePadding + y), (int)(endX - startX), (int)ContentFont.LineHeight);
            spriteBatch.Draw(unitRectangle, rectangle, SelectionColor);
        }
    }

    /// <summary>
    /// The longest substring of line starting at startIndex that still fits within maximumWidth
    /// -- the rest of the line if it already fits. Unlike a tail-anchored clip, startIndex is
    /// caller-controlled (EnsureCaretVisible), so this naturally shows whatever window currently
    /// contains the caret rather than always the very end of the text. Binary search rather than
    /// a linear scan -- called every DrawContent frame a single-line box is visible.
    /// </summary>
    private string GetVisibleWindowText(string line, int startIndex, float maximumWidth)
    {
        startIndex = System.Math.Min(startIndex, line.Length);
        if (startIndex >= line.Length)
        {
            return string.Empty;
        }

        var low = startIndex;
        var high = line.Length;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (ContentFont.MeasureString(line[startIndex..mid]).X > maximumWidth)
            {
                high = mid - 1;
            }
            else
            {
                low = mid;
            }
        }

        return line[startIndex..low];
    }

    /// <summary>
    /// A thin vertical bar at the caret's current pixel position, only while focused and mid-blink
    /// -- drawn in TextColor so it reads correctly against every box's own text/background combo
    /// (e.g. GridControl's white-on-dark search boxes vs. the Quest Composer's default palette)
    /// rather than one fixed color that might blend into either.
    /// </summary>
    private void DrawCaretIfFocused(SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!IsFocused || !_caretVisible)
        {
            return;
        }

        var position = ComputeCaretDrawPosition();
        var caretRectangle = new Rectangle((int)MathF.Round(position.X), (int)MathF.Round(position.Y), CaretWidth, (int)ContentFont.LineHeight);
        spriteBatch.Draw(unitRectangle, caretRectangle, TextColor);
    }

    /// <summary>
    /// Multiline draws in the same local (Viewport-relative) coordinates DrawContent's
    /// base.DrawContent call already uses, since it's drawn inside the same RequiresContentViewport
    /// Begin/End pair; single-line draws in absolute screen coordinates, matching its own
    /// GetVisibleWindowText draw call, and measures from _visibleStartIndex (the window's own
    /// scrolled start) rather than 0 -- a caret past the visible window's start needs its
    /// on-screen X measured from where drawing actually begins, not from the start of the whole
    /// line.
    /// </summary>
    private Vector2 ComputeCaretDrawPosition()
    {
        if (_multiline)
        {
            var lineIndex = FindLineIndexFor(_caretIndex);
            var lineStart = _lineSpans[lineIndex].Start;
            var x = ContentFont.MeasureString(OriginalText[lineStart.._caretIndex]).X;
            var y = ContentFont.LineHeight * lineIndex;
            return new Vector2(LinePadding + x, LinePadding + y);
        }

        var visibleX = ContentFont.MeasureString(OriginalText[_visibleStartIndex.._caretIndex]).X;
        return ContentAbsolutePosition + new Vector2(LinePadding + visibleX, LinePadding);
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
        var desiredWindowHeight = desiredContentHeight + BorderInsetDoubled.Y + HeaderInsetHeight;

        if (desiredWindowHeight != CurrentSize.Y)
        {
            SetSize(new Vector2(CurrentSize.X, desiredWindowHeight));

            // SetSize only re-measures this window itself. A WrapContent parent needs to be
            // told separately that a child it already contains has changed size --
            // AddChildWindow/RemoveChildWindow handle that on attach/detach, but nothing does
            // for a child resizing itself afterward. A Fixed/Fill parent doesn't need this,
            // though: its own size never depends on children, and the supported pattern for
            // sizing one around a growing TextBox (see TextBoxTests.FixedParent_...) is to
            // resize it from this box's own Resized event -- which the SetSize call above
            // already fired -- so re-measuring it again here would just be a second, redundant
            // layout pass on every keystroke that changes line count.
            if (ParentElement?.DisplayMode == ElementDisplayMode.WrapContent)
            {
                ParentElement.MeasureAndArrange();
            }
        }
    }

    /// <summary>
    /// Builds _lineSpans (OriginalText-index-aligned visual lines, never hyphenated) and derives
    /// DisplayText from them for drawing -- the reverse of TextWindow's approach, deliberately:
    /// trying to reconstruct OriginalText offsets from an already-wrapped/hyphenated string is
    /// fragile (a hyphen or an inserted '\n' shifts every later index), so _lineSpans is treated
    /// as the source of truth and FormattedText is just its '\n'-joined rendering. Segments are
    /// still split on this box's own embedded '\n' (Shift+Enter) first, same reasoning as before:
    /// word-wrap only breaks on spaces, never treats '\n' as a break on its own.
    /// </summary>
    public override void ReformatDisplayText()
    {
        _lineSpans.Clear();

        // Single-line boxes (every search box today) never wrap onto a second visual line
        // regardless of width -- matching every standard single-line text input, which
        // overflows/scrolls horizontally instead of growing taller. Multiline is the only mode
        // that word-wraps at all; OriginalText can't contain '\n' outside it either way (Shift+Enter
        // is gated behind _multiline, see OnHotkeysAction), so the segment loop below still only
        // ever produces one segment for a single-line box.
        var maximumWidth = _multiline ? _contentState.Size.X - ContentPadding.X * 2 : float.MaxValue;
        var segmentStart = 0;

        while (true)
        {
            var newlineIndex = OriginalText.IndexOf('\n', segmentStart);
            var segmentEnd = newlineIndex < 0 ? OriginalText.Length : newlineIndex;
            WrapSegment(segmentStart, segmentEnd, maximumWidth);

            if (newlineIndex < 0)
            {
                break;
            }
            segmentStart = newlineIndex + 1;
        }

        var stringBuilder = new StringBuilder();
        for (var index = 0; index < _lineSpans.Count; index++)
        {
            if (index > 0)
            {
                stringBuilder.Append('\n');
            }
            var (start, length) = _lineSpans[index];
            stringBuilder.Append(OriginalText, start, length);
        }

        DisplayText = new DisplayText(stringBuilder.ToString(), _lineSpans.Count);
    }

    /// <summary>
    /// Wraps one '\n'-free segment of OriginalText on word (space) boundaries only, appending one
    /// entry to _lineSpans per resulting visual line. A word too wide for the box even alone is
    /// hard-broken character by character (FindHardBreakIndex) rather than hyphenated -- unlike
    /// TextWindow's read-only wrap, inserting a character that doesn't exist in OriginalText
    /// would break every OriginalText-index caret/selection this exists to support.
    /// </summary>
    private void WrapSegment(int segmentStart, int segmentEnd, float maximumWidth)
    {
        if (segmentStart >= segmentEnd)
        {
            _lineSpans.Add((segmentStart, 0));
            return;
        }

        var lineStart = segmentStart;
        var lineWidth = 0f;
        var wordStart = segmentStart;

        while (wordStart < segmentEnd)
        {
            var spaceIndex = OriginalText.IndexOf(' ', wordStart, segmentEnd - wordStart);
            var wordEnd = spaceIndex < 0 ? segmentEnd : spaceIndex;
            var isFirstWordOnLine = wordStart == lineStart;
            var wordWidth = ContentFont.MeasureString(OriginalText[wordStart..wordEnd]).X;

            if (!isFirstWordOnLine)
            {
                var spaceWidth = ContentFont.MeasureString(" ").X;
                if (lineWidth + spaceWidth + wordWidth > maximumWidth)
                {
                    // Doesn't fit alongside what's already on this line -- close the line
                    // before the single space (at wordStart - 1) that would have joined this
                    // word to it, and re-evaluate this same word against a fresh line.
                    _lineSpans.Add((lineStart, wordStart - 1 - lineStart));
                    lineStart = wordStart;
                    lineWidth = 0f;
                    isFirstWordOnLine = true;
                }
                else
                {
                    lineWidth += spaceWidth + wordWidth;
                }
            }

            if (isFirstWordOnLine)
            {
                if (wordWidth > maximumWidth)
                {
                    var breakIndex = FindHardBreakIndex(wordStart, wordEnd, maximumWidth);
                    _lineSpans.Add((lineStart, breakIndex - lineStart));
                    lineStart = breakIndex;
                    wordStart = breakIndex;
                    continue;
                }

                lineWidth = wordWidth;
            }

            if (spaceIndex < 0)
            {
                _lineSpans.Add((lineStart, segmentEnd - lineStart));
                return;
            }

            wordStart = spaceIndex + 1;
        }

        // The segment ends exactly on the trailing space just consumed above (wordStart now
        // equals segmentEnd, so the while condition is false without ever reaching the
        // spaceIndex < 0 return) -- e.g. "abc " with nothing typed after the space yet. Without
        // this, the current line (up to and including that trailing space) would never be
        // flushed to _lineSpans at all, making the whole line vanish from DisplayText until a
        // non-space character gave the loop something to flush on.
        _lineSpans.Add((lineStart, segmentEnd - lineStart));
    }

    /// <summary>
    /// Finds the furthest index in [wordStart, wordEnd] whose substring from wordStart still
    /// fits within maximumWidth -- always at least wordStart + 1, so a single glyph wider than
    /// the whole box still makes forward progress instead of looping forever. Binary search
    /// rather than a linear scan -- width is monotonically non-decreasing as the substring grows.
    /// </summary>
    private int FindHardBreakIndex(int wordStart, int wordEnd, float maximumWidth)
    {
        var low = wordStart + 1;
        var high = wordEnd;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (ContentFont.MeasureString(OriginalText[wordStart..mid]).X > maximumWidth)
            {
                high = mid - 1;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (!IsFocused)
        {
            return;
        }

        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(Rectangle, FocusIndicatorThickness);
        spriteBatch.Draw(unitRectangle, top, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, bottom, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, left, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, right, FocusIndicatorColor);
    }
}
