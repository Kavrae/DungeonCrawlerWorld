using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Presentation.Input;

/// <summary>
/// Translates raw keyboard/mouse state into the app's UI-level interactions
/// </summary>
public sealed class GameInputController
{
    /// <summary>MouseState.ScrollWheelValue's units per standard wheel detent -- the FNA/XNA convention, not configurable per-device.</summary>
    private const float WheelNotchValue = 120f;

    /// <summary>Content pixels scrolled per wheel detent -- roughly three lines of the 8pt font most window content uses, matching typical OS scroll-speed defaults.</summary>
    private const float ScrollPixelsPerNotch = 24f;

    private readonly List<Window> _rootWindows;
    private readonly List<Window> _hudWindows;
    private readonly List<Window> _alwaysOnTopWindows;
    private readonly Vector2 _screenSize;

    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;

    private WindowInteraction _activeInteraction = WindowInteraction.NotHit;
    private Vector2 _dragStartMousePosition;
    private Vector2 _dragStartRelativePosition;
    private Vector2 _dragStartSize;

    /// <summary>The window a right-mouse-button drag started over (hit-tested on press), or null while no right-drag is in progress -- see HandleRightDragStart/HandleRightDrag.</summary>
    private Window? _rightDragWindow;

    /// <summary>Mouse position at the moment the current right-drag started -- HandleRightDrag reports the total delta from this anchor every frame, not a per-frame increment, so the receiving window never has to worry about drift from accumulating many small deltas.</summary>
    private Vector2 _rightDragStartMousePosition;

    private Window? _focusedWindow;

    /// <summary>
    /// The container (a parent's ChildWindows, or the always-on-top tier) the currently
    /// focused window belonged to at the moment it gained focus -- see GetSiblingContainer and
    /// RedirectFocusAwayFrom. Snapshotted in SetFocus rather than recomputed at close/minimize
    /// time because a closing window may already have removed itself from that same list by
    /// then (e.g. NotificationCenter.OnActiveNotificationClosed), depending on event
    /// subscription order.
    /// </summary>
    private List<Window>? _focusedWindowSiblings;

    /// <summary>The fallback focus target whenever a close/minimize redirect (see RedirectFocusAwayFrom) finds no sibling to move to -- e.g. dismissing the last active notification, or closing the quest composer popup. Set once via SetDefaultFocusWindow, the same composition-root role FocusWindow already plays for initial focus.</summary>
    private Window? _defaultFocusWindow;

    /// <summary>
    /// The focused window itself plus every ParentWindow above it, up to its root -- e.g. a
    /// focused TextBox's chain is [textBox, popup]. Closing a window only ever fires Closed on
    /// that exact window, never on its still-open descendants (RemoveChildWindow doesn't raise
    /// anything on the child being removed) -- so closing the quest-composer popup while its
    /// TextBox holds focus would otherwise never reach OnFocusedWindowClosed at all, since
    /// _focusedWindow (the TextBox) never itself closes. Subscribing Closed across the whole
    /// chain, not just the focused window, is what makes an ancestor closing still redirect
    /// focus away from whatever descendant currently holds it.
    /// </summary>
    private readonly List<Window> _focusedWindowAncestorChain = [];

    /// <summary>
    /// Characters typed this frame, buffered from FNA's static TextInputEXT.TextInput event
    /// (subscribed once, in the constructor) and drained by RouteTextInputToFocusedWindow.
    /// Per-instance, not static, so each GameInputController -- including the many short-lived
    /// ones tests construct -- only ever sees characters typed while it itself is subscribed.
    /// </summary>
    private readonly List<char> _pendingTextInput = [];

    /// <summary>
    /// Wraps TextInputEXT.StartTextInput/StopTextInput (see SetFocus) -- swappable in tests,
    /// since SDL_IsTextInputActive's real state isn't reliably observable in a headless test
    /// environment with no actual SDL window backing it (confirmed: asserting on it directly
    /// still reads false immediately after a real StartTextInput() call). Tests substitute a
    /// call-recording fake and assert on that instead.
    /// </summary>
    internal Action StartTextInput = TextInputEXT.StartTextInput;

    /// <summary>See StartTextInput.</summary>
    internal Action StopTextInput = TextInputEXT.StopTextInput;

    public GameInputController(List<Window> rootWindows, List<Window> hudWindows, List<Window> alwaysOnTopWindows, Vector2 screenSize)
    {
        _rootWindows = rootWindows;
        _hudWindows = hudWindows;
        _alwaysOnTopWindows = alwaysOnTopWindows;
        _screenSize = screenSize;

        // Subscribing is safe to do unconditionally and permanently -- SDL simply never raises
        // SDL_TEXTINPUT while text input is stopped (see SetFocus's Start/StopTextInput calls
        // below), so this just never fires until a TextBox is actually focused.
        TextInputEXT.TextInput += OnTextInput;
    }

    /// <summary>Internal, not private, so tests can simulate a typed character without a real OS text-input event -- the subscribed TextInputEXT.TextInput handler in real use otherwise.</summary>
    internal void OnTextInput(char character) => _pendingTextInput.Add(character);

    /// <summary>
    /// The title button currently held down by the mouse, if any -- null the rest of the
    /// time, including once the matching release fires (regardless of where the mouse ends
    /// up). Window Chrome Phase B reads this to switch the held button to an Inset look.
    /// </summary>
    internal Button? PressedButton { get; private set; }

    /// <summary>The drag/resize interaction currently in progress (or WindowInteraction.NotHit if none). Move is wired to SetRelativePosition and Resize to SetBounds, both each held frame -- see ComputeResize for the resize math.</summary>
    internal WindowInteraction ActiveInteraction => _activeInteraction;

    /// <summary>The window currently holding keyboard focus, if any -- see SetFocus/RouteKeyPressesToFocusedWindow/CycleFocus.</summary>
    internal Window? FocusedWindow => _focusedWindow;

    /// <summary>Focuses a window from outside -- GameLoop calls this once at startup to default-focus the map window, since a window's own hotkeys (see RouteHotkeysToFocusedWindow) only fire while it holds focus.</summary>
    public void FocusWindow(Window window) => SetFocus(window);

    /// <summary>See _defaultFocusWindow.</summary>
    public void SetDefaultFocusWindow(Window window) => _defaultFocusWindow = window;

    /// <summary>Window.WindowRelativePosition captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. Move's per-frame SetRelativePosition is this plus DragDelta; ComputeResize uses it as the Left/Top-edge resize baseline.</summary>
    internal Vector2 DragStartRelativePosition => _dragStartRelativePosition;

    /// <summary>Window.WindowCurrentSize captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. ComputeResize's resize baseline.</summary>
    internal Vector2 DragStartSize => _dragStartSize;

    /// <summary>Mouse movement since the drag started, recomputed every held frame -- zero on the press frame itself and once released.</summary>
    internal Vector2 DragDelta { get; private set; }

    /// <summary>The last cursor UpdateCursor set (or the initial Arrow default, if it's never had reason to change) -- lets tests assert on cursor selection without depending on real OS cursor state.</summary>
    internal MouseCursor CurrentCursor { get; private set; } = MouseCursor.Arrow;

    public void Update(GameTime gameTime) => Update(Keyboard.GetState(), Mouse.GetState());

    /// <summary>
    /// Takes explicit states rather than reading Keyboard.GetState()/Mouse.GetState() itself,
    /// so tests can drive synthetic press/release/move sequences -- the public
    /// Update(GameTime) overload above is the only real caller otherwise.
    /// </summary>
    internal void Update(KeyboardState keyboardState, MouseState mouseState)
    {
        RouteHotkeysToFocusedWindow(keyboardState);
        HandleFocusCycling(keyboardState);
        RouteKeyPressesToFocusedWindow(keyboardState);
        RouteTextInputToFocusedWindow();

        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
        {
            HandleMousePress(mouseState);
        }
        else if (mouseState.LeftButton == ButtonState.Released && _previousMouseState.LeftButton == ButtonState.Pressed)
        {
            HandleMouseRelease(mouseState);
        }
        else if (mouseState.LeftButton == ButtonState.Pressed && _activeInteraction.Kind != WindowInteractionKind.None)
        {
            HandleMouseDrag(mouseState);
        }

        if (mouseState.RightButton == ButtonState.Pressed && _previousMouseState.RightButton == ButtonState.Released)
        {
            HandleRightDragStart(mouseState);
        }
        else if (mouseState.RightButton == ButtonState.Released && _previousMouseState.RightButton == ButtonState.Pressed)
        {
            HandleRightDragEnd();
        }
        else if (mouseState.RightButton == ButtonState.Pressed && _rightDragWindow is not null)
        {
            HandleRightDrag(mouseState);
        }

        UpdateMouseWheelScroll(mouseState);
        UpdateCursor(mouseState);

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
    }

    /// <summary>
    /// Routes the whole keyboard state to whichever window is focused, once per frame (see
    /// Window.HandleHotkeys) -- e.g. MapWindow's WASD/zoom/PageUp/PageDown/Space, or a future
    /// inventory window's own navigation keys. GameInputController itself knows nothing about
    /// what any window's hotkeys are; it only knows which window is focused.
    /// </summary>
    private void RouteHotkeysToFocusedWindow(KeyboardState keyboardState) => _focusedWindow?.HandleHotkeys(keyboardState, _previousKeyboardState);

    /// <summary>Tab itself must stay unconditional -- it's how focus moves in the first place, so it can never be gated behind already holding focus.</summary>
    private void HandleFocusCycling(KeyboardState keyboardState)
    {
        if (!IsKeyPressed(keyboardState, Keys.Tab))
        {
            return;
        }

        var direction = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift)
            ? -1
            : 1;
        CycleFocus(direction);
    }

    private void HandleMousePress(MouseState mouseState)
    {
        var clickPosition = new Point(mouseState.X, mouseState.Y);

        _activeInteraction = TryHitTestInteraction(clickPosition);
        PressedButton = _activeInteraction.Button;
        PressedButton?.SetPressed(true);
        DragDelta = Vector2.Zero;

        if (_activeInteraction.Window is not null)
        {
            RaiseToFront(_activeInteraction.Window);

            if (_activeInteraction.Window.CanUserFocus)
            {
                SetFocus(_activeInteraction.Window);
            }

            if (_activeInteraction.Kind != WindowInteractionKind.None)
            {
                _dragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
                _dragStartRelativePosition = _activeInteraction.Window.WindowRelativePosition;
                _dragStartSize = _activeInteraction.Window.WindowCurrentSize;
            }
        }
    }

    /// <summary>
    /// Fires on release, not press -- standard button convention (press only starts the pressed
    /// visual; release commits, re-hit-testing the same window at the release position so a
    /// button/title/content click that's been dragged off its target quietly does nothing rather
    /// than firing against whatever else the mouse happens to be over). This is also the only way
    /// the pressed visual is ever actually observable: firing on press meant a destructive action
    /// (Close) usually destroyed the button before a held frame could even render.
    /// </summary>
    private void HandleMouseRelease(MouseState mouseState)
    {
        _activeInteraction.Window?.HandleClick(new Point(mouseState.X, mouseState.Y));

        PressedButton?.SetPressed(false);
        PressedButton = null;
        _activeInteraction = WindowInteraction.NotHit;
        DragDelta = Vector2.Zero;
    }

    private void HandleMouseDrag(MouseState mouseState)
    {
        DragDelta = new Vector2(mouseState.X, mouseState.Y) - _dragStartMousePosition;

        if (_activeInteraction.Kind == WindowInteractionKind.Move && _activeInteraction.Window is not null)
        {
            var window = _activeInteraction.Window;
            var desiredPosition = _dragStartRelativePosition + DragDelta;
            window.SetRelativePosition(ClampMoveToBounds(desiredPosition, window.WindowCurrentSize, GetPositionBounds(window)));
        }
        else if (_activeInteraction.Kind == WindowInteractionKind.Resize && _activeInteraction.Window is not null)
        {
            var window = _activeInteraction.Window;
            var (relativePosition, size) = ComputeResize(window, _activeInteraction.Edges, _dragStartRelativePosition, _dragStartSize, DragDelta);
            (relativePosition, size) = ClampResizeToBounds(relativePosition, size, GetPositionBounds(window));
            window.SetBounds(relativePosition, size);
        }
    }

    /// <summary>
    /// Captures which window a right-mouse-button drag started over, hit-testing the same way
    /// a left-click does (TryHitTestInteraction) -- but with no raise-to-front/focus side
    /// effects, since a drag-to-pan gesture shouldn't steal focus or reorder windows the way
    /// clicking one does. Null (nothing hit, e.g. empty space between windows) means the drag
    /// simply forwards to nothing until released. Also snapshots the press position as the
    /// anchor HandleRightDrag measures every subsequent frame's total delta from.
    /// </summary>
    private void HandleRightDragStart(MouseState mouseState)
    {
        var position = new Point(mouseState.X, mouseState.Y);
        _rightDragWindow = TryHitTestInteraction(position).Window;
        _rightDragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
        _rightDragWindow?.HandleRightDragStart();
    }

    /// <summary>Forwards the total mouse-pixel delta since the drag started (not this frame's increment) to whichever window the drag started over -- see Window.HandleRightDrag.</summary>
    private void HandleRightDrag(MouseState mouseState)
    {
        var totalDelta = new Vector2(mouseState.X, mouseState.Y) - _rightDragStartMousePosition;
        _rightDragWindow?.HandleRightDrag(totalDelta);
    }

    private void HandleRightDragEnd()
    {
        _rightDragWindow?.HandleRightDragEnd();
        _rightDragWindow = null;
    }

    /// <summary>
    /// Scrolls whichever window is directly under the cursor, if it opts into
    /// CanUserScrollVertical/Horizontal (see Window.ScrollBy) -- independent of
    /// ActiveInteraction, so scrolling one window mid-drag of another is harmless rather than
    /// something that needs guarding against. ScrollWheelValue is cumulative, not per-frame, so
    /// this reads like every other per-frame delta here (see the mouse-button handling above):
    /// diffed against last frame's value.
    /// </summary>
    private void UpdateMouseWheelScroll(MouseState mouseState)
    {
        var wheelDelta = mouseState.ScrollWheelValue - _previousMouseState.ScrollWheelValue;
        if (wheelDelta == 0)
        {
            return;
        }

        var position = new Point(mouseState.X, mouseState.Y);
        var hoveredInteraction = TryHitTestInteraction(position);
        if (hoveredInteraction.Window is not { } window || !(window.CanUserScrollVertical || window.CanUserScrollHorizontal))
        {
            return;
        }

        // Scrolling forward (wheelDelta > 0) moves content up (offset decreases) -- the
        // universal convention -- hence the negation. Vertical only: shift+wheel-for-horizontal
        // is a reasonable future addition, but nothing today needs it (see TextWindow, whose
        // wrapped text can only ever overflow horizontally by a single unbreakable word).
        window.ScrollBy(new Vector2(0, -wheelDelta / WheelNotchValue * ScrollPixelsPerNotch));
    }

    /// <summary>Always-on-top tier first, then HUD, then base (root) -- a higher tier can never lose to a lower one. Each tier topmost (last-raised) first.</summary>
    private WindowInteraction TryHitTestInteraction(Point position)
    {
        var interaction = TryHitTestInList(_alwaysOnTopWindows, position);
        if (interaction.Window is not null)
        {
            return interaction;
        }

        interaction = TryHitTestInList(_hudWindows, position);
        return interaction.Window is not null
            ? interaction
            : TryHitTestInList(_rootWindows, position);
    }

    private static WindowInteraction TryHitTestInList(List<Window> windows, Point position)
    {
        for (var index = windows.Count - 1; index >= 0; index--)
        {
            var interaction = windows[index].TryHitTestInteraction(position);
            if (interaction.Window is not null)
            {
                return interaction;
            }
        }

        return WindowInteraction.NotHit;
    }

    /// <summary>
    /// Raises window within its own parent's children (no-op if it has no parent -- see
    /// Window.RaiseToFront), then raises whichever top-level ancestor contains it within its
    /// own tier (rootWindows/hudWindows/alwaysOnTopWindows), so the whole subtree ends up
    /// drawn/hit-tested on top of its siblings at every level.
    /// </summary>
    private void RaiseToFront(Window window)
    {
        window.RaiseToFront();

        var rootAncestor = GetRootAncestor(window);

        if (_rootWindows.Remove(rootAncestor))
        {
            _rootWindows.Add(rootAncestor);
        }
        else if (_hudWindows.Remove(rootAncestor))
        {
            _hudWindows.Add(rootAncestor);
        }
        else if (_alwaysOnTopWindows.Remove(rootAncestor))
        {
            _alwaysOnTopWindows.Add(rootAncestor);
        }
    }

    /// <summary>Walks up ParentWindow to the top-level ancestor -- shared by RaiseToFront and CycleFocus, both of which operate on whichever root/always-on-top window a given window belongs to, not the window itself.</summary>
    private static Window GetRootAncestor(Window window)
    {
        var rootAncestor = window;
        while (rootAncestor.ParentWindow is not null)
        {
            rootAncestor = rootAncestor.ParentWindow;
        }

        return rootAncestor;
    }

    /// <summary>
    /// Subscribes to the new window's FocusRequested/DisplayModeChanged and its whole
    /// ancestor chain's Closed events (see _focusedWindowAncestorChain) so a focused window (or
    /// an ancestor of it) closing or minimizing -- and potentially being pooled and reused for
    /// something else entirely (see WindowService) -- can't leave this holding a stale
    /// reference that treats the reused instance as focused, and so a window that can't move
    /// focus itself (e.g. a TextBox submitting via Enter) can ask to be defocused in favor of
    /// another window.
    /// </summary>
    /// <remarks>
    /// Redirects into newWindow.NextTextBoxAfter(null) first, if it has any focusable TextBox
    /// children -- a window with TextBox children is never itself the terminal focus target,
    /// its first TextBox is. For every window without TextBox children (everything that
    /// existed before TextBox did) this resolves to newWindow itself, unchanged. Falls back to
    /// _defaultFocusWindow when that still leaves no target at all (newWindow itself null, e.g.
    /// RedirectFocusAwayFrom finding no sibling to move to).
    /// </remarks>
    private void SetFocus(Window? newWindow)
    {
        var target = newWindow?.NextTextBoxAfter(null) ?? newWindow ?? _defaultFocusWindow;

        if (_focusedWindow == target)
        {
            return;
        }

        // SDL's text-input mode is meant to bracket the lifetime of whatever widget is actually
        // receiving typed characters, not run for the whole app session -- left on permanently,
        // every keystroke (including e.g. MapWindow's WASD movement hotkeys) gets fed through
        // any active OS IME, popping up composition/candidate UI during ordinary gameplay, and
        // on touch/mobile SDL backends StartTextInput is also what raises the on-screen
        // keyboard. Only toggled on an actual TextBox <-> non-TextBox edge, not every focus
        // change, so tabbing between two ordinary windows doesn't touch it at all.
        if (target is TextBox && _focusedWindow is not TextBox)
        {
            StartTextInput();
        }
        else if (_focusedWindow is TextBox && target is not TextBox)
        {
            StopTextInput();
        }

        UnsubscribeFocusTracking();
        _focusedWindow?.SetFocused(false);

        _focusedWindow = target;
        _focusedWindowSiblings = target is not null
            ? GetSiblingContainer(target)
            : null;

        if (_focusedWindow is not null)
        {
            _focusedWindow.SetFocused(true);
            _focusedWindow.FocusRequested += OnFocusedWindowRequestedFocus;
            _focusedWindow.DisplayModeChanged += OnFocusedWindowDisplayModeChanged;

            for (var ancestor = _focusedWindow; ancestor is not null; ancestor = ancestor.ParentWindow)
            {
                _focusedWindowAncestorChain.Add(ancestor);
                ancestor.Closed += OnFocusedWindowClosed;
            }
        }
    }

    private void UnsubscribeFocusTracking()
    {
        if (_focusedWindow is not null)
        {
            _focusedWindow.FocusRequested -= OnFocusedWindowRequestedFocus;
            _focusedWindow.DisplayModeChanged -= OnFocusedWindowDisplayModeChanged;
        }

        foreach (var ancestor in _focusedWindowAncestorChain)
        {
            ancestor.Closed -= OnFocusedWindowClosed;
        }
        _focusedWindowAncestorChain.Clear();
    }

    private void OnFocusedWindowRequestedFocus(Window requestedWindow) => SetFocus(requestedWindow);

    /// <summary>
    /// Fires for the focused window itself closing, or any of its ancestors (see
    /// _focusedWindowAncestorChain) -- e.g. closing the quest-composer popup while its TextBox
    /// child holds focus: the popup is what actually calls Close(), the TextBox never does, so
    /// without the whole-chain subscription this would never fire at all and focus would be
    /// left dangling on a TextBox whose window is now hidden/pooled.
    /// </summary>
    private void OnFocusedWindowClosed(Window closedWindow) => RedirectFocusAwayFrom();

    /// <summary>
    /// A minimized window reads as "no longer the active thing", the same as a closed one --
    /// redirect focus the same way. Fires on every DisplayModeChanged, not just transitions
    /// into Minimized (restoring back out of it, or an unrelated Fixed/Fill change, also raise
    /// this event), so only the Minimized case is treated as a redirect trigger here. Active
    /// notification popups never hit this path -- NotificationMinimizeBehavior's "minimize"
    /// dismisses via a real Close() (see NotificationCenter.MinimizeNotification), not
    /// WindowDisplayMode.Minimized -- so OnFocusedWindowClosed above is what actually covers
    /// the notification case task 1 asked for.
    /// </summary>
    private void OnFocusedWindowDisplayModeChanged(Window window)
    {
        if (window.WindowDisplay == WindowDisplayMode.Minimized)
        {
            RedirectFocusAwayFrom();
        }
    }

    /// <summary>
    /// Moves focus to a sibling of the currently focused window, rather than leaving focus on
    /// nothing, once it (or an ancestor of it) has closed or minimized. "Sibling" is scoped to
    /// groups of genuinely interchangeable windows -- other children under the same parent
    /// (e.g. a future multi-TextBox form), or other always-on-top popups (e.g. the next active
    /// notification once the topmost one is dismissed) -- not the root tier, whose windows
    /// (map/debug/selection) are fixed, distinct panels rather than a stack of equivalent ones;
    /// closing the quest-composer popup (the only root window that can ever close) is meant to
    /// fall all the way through to _defaultFocusWindow instead of grabbing some unrelated root
    /// panel. Uses _focusedWindowSiblings (snapshotted when this window gained focus, see
    /// SetFocus) rather than re-deriving its sibling group now, since a closing window may
    /// already have removed itself from that same list by the time this runs.
    /// </summary>
    private void RedirectFocusAwayFrom()
    {
        var closingWindow = _focusedWindow;
        if (closingWindow is null)
        {
            return;
        }

        UnsubscribeFocusTracking();

        Window? nextSibling = null;
        if (_focusedWindowSiblings is not null)
        {
            foreach (var candidate in _focusedWindowSiblings)
            {
                if (candidate != closingWindow && candidate.CanUserFocus)
                {
                    nextSibling = candidate;
                }
            }
        }

        _focusedWindow = null;
        _focusedWindowSiblings = null;
        SetFocus(nextSibling);
    }

    /// <summary>See RedirectFocusAwayFrom for why this deliberately excludes the root tier.</summary>
    private List<Window>? GetSiblingContainer(Window window) =>
        window.ParentWindow?.ChildWindows
        ?? (_alwaysOnTopWindows.Contains(window) ? _alwaysOnTopWindows : null);

    /// <summary>
    /// Advances focus to the next (direction 1) or previous (direction -1) focusable Base/HUD
    /// window (Window.CanUserFocus -- e.g. the debug stats window opts out, see
    /// GameShellBootstrapper), wrapping past either end. rootWindows+hudWindows only (map/
    /// debug/selection/health bar/quest trigger -- "fixed, distinct panels"), not
    /// alwaysOnTopWindows: notifications are a separate tier dismissed via their own close/
    /// minimize button, not something a user tabs to.
    /// </summary>
    private void CycleFocus(int direction)
    {
        var focusableWindows = new List<Window>();
        foreach (var window in _rootWindows)
        {
            if (window.CanUserFocus)
            {
                focusableWindows.Add(window);
            }
        }
        foreach (var window in _hudWindows)
        {
            if (window.CanUserFocus)
            {
                focusableWindows.Add(window);
            }
        }

        if (focusableWindows.Count == 0)
        {
            return;
        }

        var currentRoot = _focusedWindow is not null
            ? GetRootAncestor(_focusedWindow)
            : null;
        var currentIndex = currentRoot is not null
            ? focusableWindows.IndexOf(currentRoot)
            : -1;

        // Nothing focused yet: forward starts at the first window, backward at the last --
        // matching standard Tab/Shift+Tab behavior from "nothing focused" -- rather than both
        // directions landing on the same window, which naive modulo wrapping from -1 would do.
        var unfocusedStartIndex = direction > 0
            ? 0
            : focusableWindows.Count - 1;
        var nextIndex = currentIndex < 0
            ? unfocusedStartIndex
            : ((currentIndex + direction) % focusableWindows.Count + focusableWindows.Count) % focusableWindows.Count;

        SetFocus(focusableWindows[nextIndex]);
    }

    /// <summary>Forwards every key newly pressed this frame to whichever window holds focus. Tab is excluded since it's already claimed above for focus-cycling.</summary>
    private void RouteKeyPressesToFocusedWindow(KeyboardState keyboardState)
    {
        if (_focusedWindow is null)
        {
            return;
        }

        foreach (var key in keyboardState.GetPressedKeys())
        {
            if (key != Keys.Tab && _previousKeyboardState.IsKeyUp(key))
            {
                _focusedWindow.HandleKeyPress(key);
            }
        }
    }

    /// <summary>
    /// Drains characters buffered by OnTextInput (see the constructor's TextInputEXT
    /// subscription) to whichever window holds focus. A separate buffer-then-drain step,
    /// unlike RouteKeyPressesToFocusedWindow's direct poll of KeyboardState, because
    /// TextInputEXT.TextInput is an event, not a per-frame state snapshot -- characters can
    /// arrive between Update calls and need to be collected rather than read live.
    /// </summary>
    private void RouteTextInputToFocusedWindow()
    {
        foreach (var character in _pendingTextInput)
        {
            _focusedWindow?.HandleTextInput(character);
        }

        _pendingTextInput.Clear();
    }

    /// <summary>
    /// Computes the relative position and size a resize drag should produce this frame. Right/
    /// Bottom grow the size directly (dragStartSize plus delta) with no position change.
    /// Left/Top must derive the position shift from the *actual clamped* size, not the raw
    /// drag delta -- otherwise the pinned (opposite) edge drifts once the drag exceeds
    /// WindowMinimumSize/WindowMaximumSize, since the position shift and the size shrink have
    /// to match exactly to keep that edge visually fixed. All four edges can combine (a corner
    /// drag sets two of them at once).
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ComputeResize(Window window, ResizeEdges edges, Vector2 dragStartRelativePosition, Vector2 dragStartSize, Vector2 dragDelta)
    {
        var relativePosition = dragStartRelativePosition;
        var size = dragStartSize;

        if (edges.HasFlag(ResizeEdges.Right))
        {
            size.X = MathHelper.Clamp(dragStartSize.X + dragDelta.X, window.WindowMinimumSize.X, window.WindowMaximumSize.X);
        }
        if (edges.HasFlag(ResizeEdges.Bottom))
        {
            size.Y = MathHelper.Clamp(dragStartSize.Y + dragDelta.Y, window.WindowMinimumSize.Y, window.WindowMaximumSize.Y);
        }
        if (edges.HasFlag(ResizeEdges.Left))
        {
            var clampedWidth = MathHelper.Clamp(dragStartSize.X - dragDelta.X, window.WindowMinimumSize.X, window.WindowMaximumSize.X);
            relativePosition.X = dragStartRelativePosition.X + (dragStartSize.X - clampedWidth);
            size.X = clampedWidth;
        }
        if (edges.HasFlag(ResizeEdges.Top))
        {
            var clampedHeight = MathHelper.Clamp(dragStartSize.Y - dragDelta.Y, window.WindowMinimumSize.Y, window.WindowMaximumSize.Y);
            relativePosition.Y = dragStartRelativePosition.Y + (dragStartSize.Y - clampedHeight);
            size.Y = clampedHeight;
        }

        return (relativePosition, size);
    }

    /// <summary>
    /// The space a window's RelativePosition/Size are measured against: a root window's is the
    /// screen itself (RelativePosition doubles as its absolute screen position, see
    /// Window.BuildWindow), a child window's is its parent's own content area (RelativePosition
    /// is relative to ContentAbsolutePosition).
    /// </summary>
    private Vector2 GetPositionBounds(Window window) => window.ParentWindow?.ContentSize ?? _screenSize;

    /// <summary>
    /// Pulls a drag-to-move's destination position back inside GetPositionBounds -- called with
    /// size unchanged, so a window dragged toward/past an edge simply stops there instead of
    /// continuing to follow the mouse off-screen (or out of its parent's content area).
    /// </summary>
    private static Vector2 ClampMoveToBounds(Vector2 position, Vector2 size, Vector2 bounds) => new(
        MathHelper.Clamp(position.X, 0, MathHelper.Max(0, bounds.X - size.X)),
        MathHelper.Clamp(position.Y, 0, MathHelper.Max(0, bounds.Y - size.Y)));

    /// <summary>
    /// Pulls a drag-to-resize's destination position+size back inside bounds. Unlike a move
    /// clamp (which only ever adjusts position), overflowing the left/top edge here must shrink
    /// the size by the overflow amount (not just clamp position) -- the window is being resized,
    /// not relocated, so running out of room at the edge being dragged should stop that edge
    /// growing further rather than sliding the whole window back on-screen. The right/bottom
    /// edges never move on their own (see ComputeResize), so overflow there is always a pure
    /// size reduction with no position change.
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ClampResizeToBounds(Vector2 relativePosition, Vector2 size, Vector2 bounds)
    {
        if (relativePosition.X < 0)
        {
            size.X += relativePosition.X;
            relativePosition.X = 0;
        }
        if (relativePosition.Y < 0)
        {
            size.Y += relativePosition.Y;
            relativePosition.Y = 0;
        }

        var rightOverflow = relativePosition.X + size.X - bounds.X;
        if (rightOverflow > 0)
        {
            size.X -= rightOverflow;
        }

        var bottomOverflow = relativePosition.Y + size.Y - bounds.Y;
        if (bottomOverflow > 0)
        {
            size.Y -= bottomOverflow;
        }

        return (relativePosition, size);
    }

    /// <summary>
    /// Sets the OS cursor for whatever's under the mouse right now: the active drag's own
    /// cursor while one is in progress (regardless of where the mouse has since wandered --
    /// e.g. a resize drag dragged inward past the border still shows the resize cursor, not
    /// whatever the mouse happens to be over), otherwise a hover hit-test. The hover hit-test
    /// is skipped when the mouse hasn't moved since last frame -- it's a full recursive
    /// Rectangle.Contains walk over every root/always-on-top window and their descendants
    /// (title buttons, tiled/floating children), which otherwise ran unconditionally every
    /// single frame regardless of whether the mouse was even moving. A window appearing/
    /// resizing/closing directly under a stationary mouse can leave the cursor stale for a
    /// frame until the mouse next moves -- an acceptable, self-correcting tradeoff for not
    /// re-walking the whole tree 60 times a second. Only calls MouseCursorEXT.SetCursor when
    /// the cursor actually changes, to avoid a native call every single frame regardless of
    /// whether anything changed.
    /// </summary>
    private void UpdateCursor(MouseState mouseState)
    {
        var position = new Point(mouseState.X, mouseState.Y);
        var previousPosition = new Point(_previousMouseState.X, _previousMouseState.Y);

        var cursor = _activeInteraction.Kind switch
        {
            WindowInteractionKind.Resize => GetResizeCursor(_activeInteraction.Edges),
            WindowInteractionKind.Move => MouseCursor.SizeAll,
            _ when position == previousPosition => CurrentCursor,
            _ => GetHoverCursor(position),
        };

        if (cursor != CurrentCursor)
        {
            MouseCursorEXT.SetCursor(cursor);
            CurrentCursor = cursor;
        }
    }

    private MouseCursor GetHoverCursor(Point position)
    {
        var interaction = TryHitTestInteraction(position);
        return interaction.Kind switch
        {
            WindowInteractionKind.Resize => GetResizeCursor(interaction.Edges),
            WindowInteractionKind.Move => MouseCursor.SizeAll,
            _ => MouseCursor.Arrow,
        };
    }

    /// <summary>Diagonal corners get the diagonal resize cursor matching that corner's axis (Top+Left/Bottom+Right = NW-SE, Top+Right/Bottom+Left = NE-SW); a single edge gets the matching straight cursor.</summary>
    private static MouseCursor GetResizeCursor(ResizeEdges edges)
    {
        if (edges == (ResizeEdges.Top | ResizeEdges.Left) || edges == (ResizeEdges.Bottom | ResizeEdges.Right))
        {
            return MouseCursor.SizeNWSE;
        }
        if (edges == (ResizeEdges.Top | ResizeEdges.Right) || edges == (ResizeEdges.Bottom | ResizeEdges.Left))
        {
            return MouseCursor.SizeNESW;
        }
        if (edges.HasFlag(ResizeEdges.Top) || edges.HasFlag(ResizeEdges.Bottom))
        {
            return MouseCursor.SizeNS;
        }
        if (edges.HasFlag(ResizeEdges.Left) || edges.HasFlag(ResizeEdges.Right))
        {
            return MouseCursor.SizeWE;
        }

        return MouseCursor.Arrow;
    }

    private bool IsKeyPressed(KeyboardState current, Keys key) => Window.WasKeyPressed(current, _previousKeyboardState, key);
}