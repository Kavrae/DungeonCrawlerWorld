using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SDL3;
using System.Text;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Content;

namespace UIEngine.UI;

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
public class TextBox(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, CursorTextContent? cursorTextContent = null, ContextMenuController? contextMenuController = null) : TextWindow(fontService, elementPoolService, renderService)
{
    private static readonly Color FocusIndicatorColor = WindowPalette.FocusAccentColor;
    private static readonly BorderThickness FocusIndicatorThickness = BorderThickness.Uniform(new Vector2(2, 2));

    /// <summary>Multiline boxes start tall enough for exactly this many lines and never shrink below it, regardless of how little text is in the box. See AutoSizeToContent.</summary>
    private const int MinimumVisibleLines = 2;

    /// <summary>Same delay-gated frame-counter idiom as HudChrome.HoverTooltipDelayFrames/DebouncedTextFilter -- toggles CaretVisible every half-second while focused.</summary>
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

    /// <summary>Raised whenever the text changes, typed or set from outside the box, with the new text.</summary>
    public event Action<string>? TextChanged;

    /// <summary>Placeholder text shown whenever the box is both empty and unfocused (see DrawContent) -- null (the default) draws nothing, so every existing TextBox usage is unaffected.</summary>
    public string? GhostText { get; set; }

    public Color GhostTextColor { get; set; } = WindowPalette.GhostText;

    /// <summary>True to show text the user can select and copy but not change: typing, Backspace, Delete, Cut and Paste do nothing, and SetText still replaces it.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>A disabled box keeps its text, draws its content background darkened (WindowPalette.DisabledBackground), and is never hit-tested, so it can't be focused, typed into or selected.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether typing, Backspace, Delete, Cut and Paste change the text: enabled and not read-only.</summary>
    protected bool AcceptsEdits => Enabled && !IsReadOnly;

    protected override bool IsHitTestable => base.IsHitTestable && Enabled;

    protected override Color ContentBackgroundColor => Enabled ? base.ContentBackgroundColor : WindowPalette.DisabledBackground(base.ContentBackgroundColor);

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _multiline = options.Text?.Multiline ?? false;

        // TextBox instances are pooled and reused -- Build is where OriginalText itself already resets for a fresh use, so
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
        GhostTextColor = WindowPalette.GhostText;
        IsReadOnly = false;
        Enabled = true;
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
            _heldFramesByRepeatableKey.Clear();
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
        if (!AcceptsEdits || char.IsControl(character) || !AcceptsCharacter(character))
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
                if (!AcceptsEdits)
                {
                    return;
                }

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
            if (CopySelectionToClipboard() && AcceptsEdits)
            {
                TryDeleteSelection();
            }
            return;
        }

        if (ctrlHeld && WasKeyPressed(keyboardState, previousKeyboardState, Keys.V))
        {
            if (AcceptsEdits)
            {
                PasteFromClipboard();
            }

            return;
        }

        // Backspace/Delete/Left/Right/Up/Down all repeat while held (ShouldFire), instead of the
        // edit-hook's own edge-triggered HandleKeyPress -- see ShouldFire's own doc comment for
        // why Backspace/Delete moved here alongside the arrows rather than staying split across
        // two different input hooks with two different repeat behaviors.
        if (ShouldFire(Keys.Back, keyboardState) && AcceptsEdits && !TryDeleteSelection())
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

        if (ShouldFire(Keys.Delete, keyboardState) && AcceptsEdits && !TryDeleteSelection())
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
            if (!char.IsControl(character) && AcceptsCharacter(character))
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

    /// <summary>Opens a Cut/Copy/Paste/Select All context menu at mousePosition -- see BuildContextMenuOptions for the actual option list. No-op if no ContextMenuController was wired (test setups that don't build one, matching cursorTextContent's own optional-dependency convention above).</summary>
    protected override void OnRightClickTapAction(Point mousePosition)
    {
        contextMenuController?.Open(new Vector2(mousePosition.X, mousePosition.Y), BuildContextMenuOptions());
    }

    /// <summary>
    /// Cut/Copy/Paste/Select All, with the same standard Windows hotkey text shown alongside
    /// each label -- internal, not private, so tests can inspect the exact option list/enabled
    /// state without a live ContextMenuController. Each OnSelect calls the exact same private
    /// method its own Ctrl+X/C/V/A handler in OnHotkeysAction already does -- this is a second
    /// trigger for existing behavior, not a second implementation of it. Cut/Copy are enabled
    /// only with an active selection (the same check TryDeleteSelection/CopySelectionToClipboard
    /// already gate on), Paste only when the clipboard actually has text, and Select All whenever
    /// there's anything to select.
    /// </summary>
    internal IReadOnlyList<DropdownOption> BuildContextMenuOptions()
    {
        var hasSelection = _selectionAnchor is { } anchor && anchor != _caretIndex;

        return
        [
            new DropdownOption("Cut", "Ctrl+X", hasSelection && AcceptsEdits, () =>
            {
                if (CopySelectionToClipboard())
                {
                    TryDeleteSelection();
                }
            }),
            new DropdownOption("Copy", "Ctrl+C", hasSelection, () => CopySelectionToClipboard()),
            new DropdownOption("Paste", "Ctrl+V", AcceptsEdits && SDL.SDL_HasClipboardText(), PasteFromClipboard),
            new DropdownOption("Select All", "Ctrl+A", OriginalText.Length > 0, () =>
            {
                MoveCaretTo(OriginalText.Length);
                _selectionAnchor = 0;
            }),
        ];
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
        var clickedIndex = HitTestCaretIndex(mousePosition, roundToNearest: true);
        var clickedCharacterIndex = HitTestCaretIndex(mousePosition, roundToNearest: false);
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

        switch (RegisterClickForMultiClick(clickedCharacterIndex))
        {
            case 2:
                SelectWord(clickedCharacterIndex);
                break;
            case 3:
                SelectLine(clickedCharacterIndex);
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
    /// <remarks>roundToNearest picks the caret boundary nearest the pointer; false picks the character under it (word and line selection).</remarks>
    private int HitTestCaretIndex(Point mousePosition, bool roundToNearest)
    {
        if (_multiline)
        {
            var localX = mousePosition.X - ContentAbsolutePosition.X + ScrollOffset.X - LinePadding;
            var localY = mousePosition.Y - ContentAbsolutePosition.Y + ScrollOffset.Y - LinePadding;
            var lineIndex = System.Math.Clamp((int)(localY / ContentFont.LineHeight), 0, _lineSpans.Count - 1);
            var (lineStart, lineLength) = _lineSpans[lineIndex];
            return lineStart + FindColumnForPixelX(lineStart, lineLength, localX, roundToNearest);
        }

        var visibleX = mousePosition.X - ContentAbsolutePosition.X - LinePadding;
        return _visibleStartIndex + FindColumnForPixelX(_visibleStartIndex, OriginalText.Length - _visibleStartIndex, visibleX, roundToNearest);
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

    /// <summary>Selects the run of the character under the pointer -- a word, or the whitespace between two words -- never crossing a line break.</summary>
    /// <remarks>A click past the end of a line (on its '\n' or past the end of the text) selects the run the line ends with.</remarks>
    private void SelectWord(int clickedCharacterIndex)
    {
        var index = clickedCharacterIndex;
        if (index > 0 && (index >= OriginalText.Length || OriginalText[index] == '\n') && OriginalText[index - 1] != '\n')
        {
            index--;
        }

        if (index >= OriginalText.Length || OriginalText[index] == '\n')
        {
            MoveCaretTo(index);
            return;
        }

        var isWhiteSpace = char.IsWhiteSpace(OriginalText[index]);
        var wordStart = index;
        while (wordStart > 0 && IsSameRun(OriginalText[wordStart - 1], isWhiteSpace))
        {
            wordStart--;
        }

        var wordEnd = index + 1;
        while (wordEnd < OriginalText.Length && IsSameRun(OriginalText[wordEnd], isWhiteSpace))
        {
            wordEnd++;
        }

        MoveCaretTo(wordEnd);
        _selectionAnchor = wordStart;
    }

    private static bool IsSameRun(char character, bool runIsWhiteSpace) => character != '\n' && char.IsWhiteSpace(character) == runIsWhiteSpace;

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
        var index = HitTestCaretIndex(pressPosition, roundToNearest: true);
        MoveCaretTo(index);
        _selectionAnchor = index;
    }

    /// <summary>Extends the selection anchored by BeginSelectionDrag to currentPosition -- called every frame the drag continues, the click-drag equivalent of Shift+click's own anchor-capture/MoveCaretTo/anchor-restore shape.</summary>
    internal void ExtendSelectionDrag(Point currentPosition)
    {
        var anchor = _selectionAnchor ?? _caretIndex;
        MoveCaretTo(HitTestCaretIndex(currentPosition, roundToNearest: true));
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
    /// _caretIndex) if the substring from the window's start to the caret no longer fits -- then
    /// back left as far as the rest of the text still fits, so text deleted from the end or a
    /// wider box never leaves earlier text hidden beside empty space.
    /// Single-line only; see _visibleStartIndex's own doc comment.
    /// </summary>
    /// <remarks>Also runs from ReformatDisplayText, between an edit's UpdateText and its MoveCaretTo, so the caret is clamped to the new text here.</remarks>
    private void EnsureCaretVisible()
    {
        var textLength = OriginalText.Length;
        var caretIndex = System.Math.Min(_caretIndex, textLength);
        var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
        _visibleStartIndex = System.Math.Clamp(_visibleStartIndex, 0, textLength);

        if (caretIndex < _visibleStartIndex)
        {
            _visibleStartIndex = caretIndex;
        }
        else if (MeasureOriginalText(_visibleStartIndex, caretIndex) > maximumWidth)
        {
            var low = _visibleStartIndex;
            var high = caretIndex;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (MeasureOriginalText(mid, caretIndex) > maximumWidth)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            _visibleStartIndex = low;
            return;
        }

        if (_visibleStartIndex == 0 || MeasureOriginalText(_visibleStartIndex, textLength) > maximumWidth)
        {
            return;
        }

        var retreatLow = 0;
        var retreatHigh = _visibleStartIndex;
        while (retreatLow < retreatHigh)
        {
            var mid = retreatLow + (retreatHigh - retreatLow) / 2;
            if (MeasureOriginalText(mid, textLength) > maximumWidth)
            {
                retreatLow = mid + 1;
            }
            else
            {
                retreatHigh = mid;
            }
        }

        _visibleStartIndex = retreatLow;
    }

    /// <summary>Multiline analogue of EnsureCaretVisible -- scrolls vertically (ScrollBy) just far enough to keep the caret's own visual line, with the LinePadding it is drawn inside, within the currently visible content height.</summary>
    private void EnsureCaretVisibleMultiline()
    {
        var lineIndex = FindLineIndexFor(_caretIndex);
        var caretTop = ContentFont.LineHeight * lineIndex;
        var caretBottom = caretTop + ContentFont.LineHeight + LinePadding * 2;

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
        return targetStart + FindColumnForPixelX(targetStart, targetLength, desiredX, roundToNearest: true);
    }

    private float CaretPixelXWithinLine(int lineIndex, int caretIndex)
    {
        var lineStart = _lineSpans[lineIndex].Start;
        return MeasureOriginalText(lineStart, caretIndex);
    }

    /// <summary>
    /// The column (0..lineLength) within [lineStart, lineStart + lineLength) at targetX -- the
    /// inverse of CaretPixelXWithinLine. roundToNearest picks the column boundary nearest targetX
    /// (caret placement); otherwise the furthest column whose width is still &lt;= targetX, i.e.
    /// the character under it. Binary search rather than a linear scan -- called on every
    /// click/Up/Down, and width is monotonically non-decreasing as the substring grows.
    /// </summary>
    private int FindColumnForPixelX(int lineStart, int lineLength, float targetX, bool roundToNearest)
    {
        var low = 0;
        var high = lineLength;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (MeasureOriginalText(lineStart, lineStart + mid) > targetX)
            {
                high = mid - 1;
            }
            else
            {
                low = mid;
            }
        }

        if (!roundToNearest || low >= lineLength)
        {
            return low;
        }

        var widthBefore = MeasureOriginalText(lineStart, lineStart + low);
        var widthAfter = MeasureOriginalText(lineStart, lineStart + low + 1);
        return targetX - widthBefore > widthAfter - targetX ? low + 1 : low;
    }

    /// <summary>Skips any whitespace immediately before fromIndex, then the contiguous non-whitespace run before that -- the standard Ctrl+Left word-jump shape, also shared by Ctrl+Backspace.</summary>
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

    /// <summary>Whether a typed or pasted character goes into the text -- every printable one by default; a number field takes only what a number is written with.</summary>
    protected virtual bool AcceptsCharacter(char character) => true;

    /// <summary>Replaces the whole text, clearing any selection and putting the caret at the end -- for a value set from outside the box.</summary>
    public void SetText(string text)
    {
        SetTextAndAutoSize(text);
        MoveCaretTo(text.Length);
    }

    private void SetTextAndAutoSize(string newText)
    {
        var changed = newText != OriginalText;
        UpdateText(newText);
        AutoSizeToContent();
        if (changed)
        {
            TextChanged?.Invoke(newText);
        }
    }

    /// <summary>
    /// Ghost text hides the moment the box has real content OR gains focus (not just once
    /// something's typed) -- click a search box and the placeholder is
    /// gone immediately, even before the first keystroke. A single-line box with overflowing real
    /// text draws only the window starting at _visibleStartIndex that fits (GetVisibleWindowText)
    /// instead of the full line, so it clips at its own left edge and keeps scrolling to follow
    /// the caret -- matching standard text-input behavior. The caret itself (DrawCaretIfFocused)
    /// draws last so it's never hidden behind either branch's text.
    /// </summary>
    /// <remarks>
    /// Doesn't use the shared scrollable-window scroll and clip (Element.Draw's
    /// RequiresContentViewport path, driven by CanUserScrollHorizontal/Vertical) for the
    /// single-line case: it scrolls its text itself. Multiline opts into that
    /// machinery via CanUserScrollVertical (word-wrap already keeps every line within the content width, so
    /// there's nothing horizontal left to clip). Both draw in local coordinates, like every DrawContent.
    /// </remarks>
    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (string.IsNullOrEmpty(OriginalText) && !IsFocused && !string.IsNullOrEmpty(GhostText))
        {
            LabelRenderer.Draw(spriteBatch, ContentFont, GhostText, new Vector2(LinePadding, LinePadding), GhostTextColor);
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
            LabelRenderer.Draw(spriteBatch, ContentFont, visibleText, new Vector2(LinePadding, LinePadding), TextColor);
        }

        DrawCaretIfFocused(spriteBatch, unitRectangle);
    }

    /// <summary>Draws a translucent highlight behind the current selection, if any -- called before the text itself so the highlight sits behind the glyphs, not on top of them.</summary>
    /// <remarks>Drawn in DrawContent's local coordinates. The rectangles are kept until the text, its wrapping, the selection, the scroll, the width or the font changes.</remarks>
    private void DrawSelectionIfAny(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (_selectionAnchor is not { } anchor || anchor == _caretIndex)
        {
            return;
        }

        var selectionStart = System.Math.Min(anchor, _caretIndex);
        var selectionEnd = System.Math.Max(anchor, _caretIndex);
        var maximumWidth = _contentState.Size.X - ContentPadding.X * 2;
        var selectionRectangles = _selectionRectangles.Get(
            (OriginalText, DisplayText.FormattedText, _multiline, _visibleStartIndex, selectionStart, selectionEnd, maximumWidth, ContentFont),
            this,
            static (key, textBox) => key.Multiline
                ? textBox.BuildMultilineSelectionRectangles(key.SelectionStart, key.SelectionEnd)
                : textBox.BuildSingleLineSelectionRectangles(key.SelectionStart, key.SelectionEnd, key.MaximumWidth));

        foreach (var rectangle in selectionRectangles)
        {
            spriteBatch.Draw(unitRectangle, rectangle, SelectionColor);
        }
    }

    /// <summary>The current selection's highlight relative to the content origin -- none, or one rectangle clipped to the visible window.</summary>
    private Rectangle[] BuildSingleLineSelectionRectangles(int selectionStart, int selectionEnd, float maximumWidth)
    {
        var visibleSelectionStart = System.Math.Max(selectionStart, _visibleStartIndex);
        if (visibleSelectionStart >= selectionEnd)
        {
            return [];
        }

        var startX = System.Math.Min(MeasureOriginalText(_visibleStartIndex, visibleSelectionStart), maximumWidth);
        var endX = System.Math.Min(MeasureOriginalText(_visibleStartIndex, selectionEnd), maximumWidth);

        if (endX <= startX)
        {
            return [];
        }

        return [new Rectangle((int)(LinePadding + startX), LinePadding, (int)(endX - startX), (int)ContentFont.LineHeight)];
    }

    /// <summary>One highlight rectangle per spanned visual line -- the first line from selectionStart to its own end, middle lines full width, the last line from its own start to selectionEnd -- relative to the content origin.</summary>
    private Rectangle[] BuildMultilineSelectionRectangles(int selectionStart, int selectionEnd)
    {
        var startLine = FindLineIndexFor(selectionStart);
        var endLine = FindLineIndexFor(selectionEnd);
        var rectangles = new List<Rectangle>(endLine - startLine + 1);

        for (var lineIndex = startLine; lineIndex <= endLine; lineIndex++)
        {
            var (lineStart, lineLength) = _lineSpans[lineIndex];
            var rangeStart = lineIndex == startLine ? selectionStart : lineStart;
            var rangeEnd = lineIndex == endLine ? selectionEnd : lineStart + lineLength;

            if (rangeEnd <= rangeStart)
            {
                continue;
            }

            var startX = MeasureOriginalText(lineStart, rangeStart);
            var endX = MeasureOriginalText(lineStart, rangeEnd);
            var y = ContentFont.LineHeight * lineIndex;

            rectangles.Add(new Rectangle((int)(LinePadding + startX), (int)(LinePadding + y), (int)(endX - startX), (int)ContentFont.LineHeight));
        }

        return [.. rectangles];
    }

    /// <summary>DrawSelectionIfAny's last rectangles, keyed on what they were built from.</summary>
    private readonly KeyedCache<(string Text, string FormattedText, bool Multiline, int VisibleStartIndex, int SelectionStart, int SelectionEnd, float MaximumWidth, SpriteFontBase Font), Rectangle[]> _selectionRectangles = new();

    /// <summary>
    /// The longest substring of line starting at startIndex that still fits within maximumWidth
    /// -- the rest of the line if it already fits. Unlike a tail-anchored clip, startIndex is
    /// caller-controlled (EnsureCaretVisible), so this naturally shows whatever window currently
    /// contains the caret rather than always the very end of the text. Binary search rather than
    /// a linear scan, measuring spans.
    /// </summary>
    /// <remarks>Called every DrawContent frame a single-line box is visible, so the answer is kept until the line, the start index, the width or the font changes.</remarks>
    private string GetVisibleWindowText(string line, int startIndex, float maximumWidth)
    {
        if (startIndex >= line.Length)
        {
            return string.Empty;
        }

        return _visibleWindowText.Get((line, startIndex, maximumWidth, ContentFont), FontService, static (key, fontService) => ComputeVisibleWindowText(fontService.GetTextMeasurer(key.Font), key.Line, key.StartIndex, key.MaximumWidth));
    }

    private static string ComputeVisibleWindowText(FontStashTextMeasurer textMeasurer, string line, int startIndex, float maximumWidth)
    {
        var low = startIndex;
        var high = line.Length;

        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (textMeasurer.MeasureWidth(line.AsSpan(startIndex, mid - startIndex)) > maximumWidth)
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

    /// <summary>GetVisibleWindowText's last answer, keyed on what it was computed from.</summary>
    private readonly KeyedCache<(string Line, int StartIndex, float MaximumWidth, SpriteFontBase Font), string> _visibleWindowText = new();

    /// <summary>ContentFont's text measurer, looked up again only when ContentFont changes.</summary>
    private FontStashTextMeasurer ContentTextMeasurer
    {
        get
        {
            if (_contentTextMeasurer is null || !ReferenceEquals(_contentTextMeasurerFont, ContentFont))
            {
                _contentTextMeasurerFont = ContentFont;
                _contentTextMeasurer = FontService.GetTextMeasurer(ContentFont);
            }

            return _contentTextMeasurer;
        }
    }

    private SpriteFontBase? _contentTextMeasurerFont;
    private FontStashTextMeasurer? _contentTextMeasurer;

    /// <summary>Pixel width of OriginalText[startIndex..endIndex], measured as a span so no substring is allocated.</summary>
    private float MeasureOriginalText(int startIndex, int endIndex) =>
        ContentTextMeasurer.MeasureWidth(OriginalText.AsSpan(startIndex, endIndex - startIndex));

    /// <summary>
    /// A thin vertical bar at the caret's current pixel position, only while focused and mid-blink
    /// -- drawn in TextColor so it reads correctly against every box's own text/background combo
    /// (white-on-dark search boxes and the default palette alike)
    /// rather than one fixed color that might blend into either.
    /// </summary>
    /// <remarks>The offset from the content origin is kept until the text, its wrapping, the scroll, the caret or the font changes.</remarks>
    private void DrawCaretIfFocused(SpriteEffectBatch spriteBatch, Texture2D unitRectangle)
    {
        if (!IsFocused || !_caretVisible)
        {
            return;
        }

        var caretOffset = _caretOffset.Get(
            (OriginalText, DisplayText.FormattedText, _multiline, _visibleStartIndex, _caretIndex, ContentFont),
            this,
            static (_, textBox) => textBox.ComputeCaretOffset());
        var position = caretOffset;
        var caretRectangle = new Rectangle((int)MathF.Round(position.X), (int)MathF.Round(position.Y), CaretWidth, (int)ContentFont.LineHeight);
        spriteBatch.Draw(unitRectangle, caretRectangle, TextColor);
    }

    /// <summary>
    /// The caret's position relative to the content origin, in DrawContent's local coordinates like
    /// the text it sits in; single-line measures from
    /// _visibleStartIndex (the window's own scrolled start) rather than 0 -- a caret past the
    /// visible window's start needs its X measured from where drawing actually begins, not from
    /// the start of the whole line.
    /// </summary>
    private Vector2 ComputeCaretOffset()
    {
        if (_multiline)
        {
            var lineIndex = FindLineIndexFor(_caretIndex);
            var lineStart = _lineSpans[lineIndex].Start;
            var x = MeasureOriginalText(lineStart, _caretIndex);
            var y = ContentFont.LineHeight * lineIndex;
            return new Vector2(LinePadding + x, LinePadding + y);
        }

        var visibleX = MeasureOriginalText(_visibleStartIndex, _caretIndex);
        return new Vector2(LinePadding + visibleX, LinePadding);
    }

    /// <summary>DrawCaretIfFocused's last offset, keyed on what it was computed from.</summary>
    private readonly KeyedCache<(string Text, string FormattedText, bool Multiline, int VisibleStartIndex, int CaretIndex, SpriteFontBase Font), Vector2> _caretOffset = new();

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
        var spaceWidth = ContentTextMeasurer.MeasureWidth(" ");
        var segmentStart = 0;

        while (true)
        {
            var newlineIndex = OriginalText.IndexOf('\n', segmentStart);
            var segmentEnd = newlineIndex < 0 ? OriginalText.Length : newlineIndex;
            WrapSegment(segmentStart, segmentEnd, maximumWidth, spaceWidth);

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

        if (!_multiline)
        {
            EnsureCaretVisible();
        }
    }

    /// <summary>
    /// Wraps one '\n'-free segment of OriginalText on word (space) boundaries only, appending one
    /// entry to _lineSpans per resulting visual line. A word too wide for the box even alone is
    /// hard-broken character by character (FindHardBreakIndex) rather than hyphenated -- unlike
    /// TextWindow's read-only wrap, inserting a character that doesn't exist in OriginalText
    /// would break every OriginalText-index caret/selection this exists to support.
    /// </summary>
    private void WrapSegment(int segmentStart, int segmentEnd, float maximumWidth, float spaceWidth)
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
            var wordWidth = MeasureOriginalText(wordStart, wordEnd);

            if (!isFirstWordOnLine)
            {
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
            if (MeasureOriginalText(wordStart, mid) > maximumWidth)
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

        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(Rectangle, FocusIndicatorThickness);
        spriteBatch.Draw(unitRectangle, top, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, bottom, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, left, FocusIndicatorColor);
        spriteBatch.Draw(unitRectangle, right, FocusIndicatorColor);
    }
}
