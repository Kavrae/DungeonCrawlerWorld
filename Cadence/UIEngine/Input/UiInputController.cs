using Engine.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Geometry;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>
/// Translates raw keyboard/mouse state into the app's UI-level interactions. Elements live in
/// UiLayer tiers (see its own doc comment for what each one holds); a higher tier always wins
/// hit-testing over a lower one, regardless of screen position -- User (drag feedback) is
/// deliberately excluded from that priority mattering in practice, since nothing placed there
/// today is itself interactive.
/// </summary>
public sealed class UiInputController
{
    /// <summary>MouseState.ScrollWheelValue's units per standard wheel detent -- the FNA/XNA convention, not configurable per-device.</summary>
    private const float WheelNotchValue = 120f;

    /// <summary>Content pixels scrolled per wheel detent -- roughly three lines of the 8pt font most window content uses, matching typical OS scroll-speed defaults.</summary>
    private const float ScrollPixelsPerNotch = 24f;

    /// <summary>How long Escape must be held (not just tapped) before it closes every closeable DynamicHUD window at once instead of just the topmost -- see HandleEscape. Comfortably longer than DoubleClickWindowFrames-style thresholds elsewhere (0.3s), so an ordinary tap can never accidentally read as a hold.</summary>
    private static readonly int EscapeHoldCloseAllFrames = GameTiming.FramesForSeconds(0.5f);

    /// <summary>
    /// Shared single/double-click timing window, ~300ms -- the one place this value lives.
    /// Public so a host's own keyboard double-tap uses the same window, and the two gestures agree.
    /// See DispatchClick/HandleDoubleClickAwareClick.
    /// </summary>
    public static readonly int DoubleClickWindowFrames = GameTiming.FramesForSeconds(0.3f);

    /// <summary>Consecutive frames Escape has been held down, 0 while it's up -- HandleEscape's own edge/hold distinction (1 == a fresh press).</summary>
    private int _escapeHeldFrames;

    /// <summary>Guards UiLayerStack.CloseAllClosableWindows to firing once per hold, not every frame past EscapeHoldCloseAllFrames -- reset the moment Escape is released.</summary>
    private bool _escapeHoldCloseAllFired;

    /// <summary>Advances once per Update -- the frame clock DispatchClick's deferred single-click/double-click state machine measures its own window against. See FlushExpiredPendingClicks.</summary>
    private int _frameCounter;

    /// <summary>
    /// Elements with a pending, not-yet-fired single click -- see DispatchClick/
    /// HandleDoubleClickAwareClick. Only ever populated for an element that opted into double-click
    /// detection (Element.WantsDoubleClickDetection); every other click bypasses this dictionary
    /// entirely and fires Clicked immediately, same as always.
    /// </summary>
    private readonly Dictionary<Element, int> _pendingSingleClickFrameByElement = [];

    private readonly UiLayerStack _layers;
    private Vector2 _screenSize;

    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;

    private ElementInteraction _activeInteraction = ElementInteraction.NotHit;
    private Vector2 _dragStartMousePosition;
    private Vector2 _dragStartRelativePosition;
    private Vector2 _dragStartSize;

    /// <summary>Element.ScrollOffset captured at the start of a scrollbar thumb drag -- see Element.DragScrollbarThumb.</summary>
    private Vector2 _dragStartScrollOffset;

    /// <summary>Frames the mouse has been held on a scrollbar track since the press that paged it once.</summary>
    private int _scrollTrackHeldFrames;

    /// <summary>Which way the current scrollbar track press pages: -1 backward, 1 forward, 0 not at all (it landed level with the thumb).</summary>
    private int _scrollTrackPageDirection;

    /// <summary>Held this long on a scrollbar track, paging starts repeating -- the same timing as TextBox's key repeat.</summary>
    private static readonly int ScrollTrackRepeatInitialDelayFrames = GameTiming.FramesForSeconds(0.4f);

    private static readonly int ScrollTrackRepeatIntervalFrames = GameTiming.FramesForSeconds(0.05f);

    /// <summary>The element whose scrollbar is currently under the cursor, if any -- see SetHoveredScrollbar.</summary>
    private Element? _hoveredScrollbarElement;

    /// <summary>The element a right-mouse-button drag started over (hit-tested on press), or null while no right-drag is in progress -- see HandleRightDragStart/HandleRightDrag.</summary>
    private Element? _rightDragElement;

    /// <summary>Mouse position at the moment the current right-drag started -- HandleRightDrag reports the total delta from this anchor every frame, not a per-frame increment, so the receiving element never has to worry about drift from accumulating many small deltas.</summary>
    private Vector2 _rightDragStartMousePosition;

    /// <summary>Pixel distance past which a right-button press/release reads as a drag rather than a tap -- see HandleRightDrag/HandleRightDragEnd. Small enough to absorb ordinary click jitter, comfortably smaller than an intentional pan.</summary>
    private const float RightClickTapThresholdPixels = 4f;

    /// <summary>Set once the current right-drag's total delta has ever exceeded RightClickTapThresholdPixels -- checked (not recomputed) at release, so a drag that wandered out past the threshold and back before releasing still counts as a drag, not a tap.</summary>
    private bool _rightDragExceededTapThreshold;

    /// <summary>
    /// The TextBox a left-button press landed on with Kind None, if any -- drives click-drag text
    /// selection (HandleTextSelectionDrag). Mirrors the right-drag pattern above
    /// (_rightDragElement/HandleRightDragStart/HandleRightDrag) rather than Move/Resize's
    /// ElementDragInteractionKind or the content-drag mechanism: neither of those
    /// fits a text-selection gesture, and a click-drag on a TextBox always resolves to Kind None
    /// (it's a plain content click, not a window-chrome drag).
    /// </summary>
    private TextBox? _textSelectionDragBox;

    private Vector2 _textSelectionDragStartMousePosition;

    /// <summary>Same reasoning/value as RightClickTapThresholdPixels/ContentDragTapThresholdPixels -- small enough to absorb ordinary click jitter, comfortably smaller than an intentional drag-select.</summary>
    private const float TextSelectionDragTapThresholdPixels = 4f;

    /// <summary>Set once the current text-selection drag has moved past TextSelectionDragTapThresholdPixels -- gates both when BeginSelectionDrag actually fires (so a plain click never touches selection state through this path) and, on release, whether the ordinary content-click (which would otherwise collapse the selection right back down) is skipped -- see HandleMouseRelease.</summary>
    private bool _textSelectionDragExceededTapThreshold;

    /// <summary>
    /// The content drag in progress, if any: what the pressed IContentDragSource picked up (see TryStartContentDrag),
    /// resolved against whatever's under the cursor on release by the IContentDropHandlers (see ResolveContentDrag).
    /// Independent of _activeInteraction.Kind (which stays None for a plain content click on a drag source) -- tracked
    /// entirely by its own fields instead.
    /// </summary>
    private ContentDragStart? _contentDrag;

    /// <summary>The last drag's own on-screen size at the moment it started -- the ghost is drawn at this size rather than one fixed size for every drag, so it doesn't visibly jump in scale relative to wherever it was picked up from.</summary>
    private Vector2 _contentDragSourceSize;

    /// <summary>Mouse position when the current content-drag started -- ResolveContentDrag only actually resolves a drop if the release is at least ContentDragTapThresholdPixels away, so a plain click on a drag source doesn't spuriously drop it where it already is.</summary>
    private Vector2 _contentDragStartMousePosition;

    /// <summary>Same reasoning/value as RightClickTapThresholdPixels -- small enough to absorb ordinary click jitter, comfortably smaller than an intentional drag.</summary>
    private const float ContentDragTapThresholdPixels = 4f;

    /// <summary>Frames since the current content-drag payload was captured (see TryStartContentDrag) -- reset there, incremented once per Update while a payload is held. Gates ContentDragGhostVisible below.</summary>
    private int _contentDragHeldFrames;

    private static readonly int ContentDragGhostDelayFrames = GameTiming.FramesForSeconds(0.15f);

    /// <summary>The application's own raw-input rules, called in this order at fixed points of every Update -- see IUiInputHandler. An array, so the per-frame loops over it allocate no enumerator.</summary>
    /// <remarks>Replaced whole by AddInputHandler and RemoveInputHandler, never written in place, so a handler that adds or removes one mid-loop doesn't disturb the loop it runs in.</remarks>
    private IUiInputHandler[] _inputHandlers;

    /// <summary>What a content drop means, tried in this order on release until one claims it -- see IContentDropHandler.</summary>
    private readonly IContentDropHandler[] _contentDropHandlers;

    /// <summary>Owns the single shared Dropdown popup -- null in test setups that don't build one, in which case no context menu or dropdown ever opens and an already-open one (there can't be, without this) never needs dismissing.</summary>
    private readonly DropdownController? _dropdownController;

    /// <summary>Rewritten at the end of every Update -- see PointerState.</summary>
    private readonly PointerState _pointerState;

    /// <summary>UiLayerStack.IsMenuWindow and IsMenuModeExempt as the predicates TryHitTestWhere takes -- made once, since converting a method group allocates a delegate each time.</summary>
    private readonly Func<Element, bool> _isMenuWindow;

    private readonly Func<Element, bool> _isMenuModeExempt;

    private Element? _focusedElement;

    /// <summary>
    /// The container (a parent's ChildElements, or the DynamicHUD tier) the currently focused
    /// element belonged to at the moment it gained focus -- see GetSiblingContainer and
    /// RedirectFocusAwayFrom. Snapshotted in SetFocus rather than recomputed at close/minimize
    /// time because a closing element may already have removed itself from that same list by
    /// then, depending on event subscription order.
    /// </summary>
    private IReadOnlyList<Element>? _focusedElementSiblings;

    /// <summary>The fallback focus target whenever a close/minimize redirect (see RedirectFocusAwayFrom) finds no sibling to move to -- e.g. dismissing the last open popup. Set once via SetDefaultFocusElement, the same composition-root role FocusElement already plays for initial focus.</summary>
    private Element? _defaultFocusElement;

    /// <summary>
    /// The focused element itself plus every ParentElement above it, up to its root -- e.g. a
    /// focused TextBox's chain is [textBox, popup]. Closing a window only ever fires Closed on
    /// that exact element, never on its still-open descendants (RemoveChildWindow doesn't raise
    /// anything on the child being removed) -- so closing a popup while its
    /// TextBox holds focus would otherwise never reach OnFocusedElementClosed at all, since
    /// _focusedElement (the TextBox) never itself closes. Subscribing Closed across the whole
    /// chain, not just the focused element, is what makes an ancestor closing still redirect
    /// focus away from whatever descendant currently holds it.
    /// </summary>
    private readonly List<Element> _focusedElementAncestorChain = [];

    /// <summary>
    /// Characters typed this frame, buffered from FNA's static TextInputEXT.TextInput event
    /// (subscribed once, in the constructor) and drained by RouteTextInputToFocusedElement.
    /// Per-instance, not static, so each UiInputController -- including the many short-lived
    /// ones tests construct -- only ever sees characters typed while it itself is subscribed.
    /// </summary>
    private readonly List<char> _pendingTextInput = [];

    /// <summary>
    /// Wraps TextInputEXT.StartTextInput/StopTextInput (see SetFocus) -- swappable in tests,
    /// since SDL_IsTextInputActive's real state isn't reliably observable in a headless test
    /// environment with no actual SDL window backing it (it reads false even right after a real
    /// StartTextInput() call). Tests substitute a
    /// call-recording fake and assert on that instead.
    /// </summary>
    internal Action StartTextInput = TextInputEXT.StartTextInput;

    /// <summary>See StartTextInput.</summary>
    internal Action StopTextInput = TextInputEXT.StopTextInput;

    /// <param name="inputHandlers">The application's raw-input rules, called in this order -- see IUiInputHandler.</param>
    /// <param name="contentDropHandlers">What a content drop means, tried in this order until one claims it -- see IContentDropHandler.</param>
    public UiInputController(UiLayerStack layers, Vector2 screenSize, PointerState pointerState, IReadOnlyList<IUiInputHandler> inputHandlers, IReadOnlyList<IContentDropHandler> contentDropHandlers, DropdownController? dropdownController = null)
    {
        _layers = layers;
        _screenSize = screenSize;
        _pointerState = pointerState;
        _inputHandlers = [.. inputHandlers];
        _contentDropHandlers = [.. contentDropHandlers];
        _dropdownController = dropdownController;
        _isMenuWindow = layers.IsMenuWindow;
        _isMenuModeExempt = layers.IsMenuModeExempt;

        // Subscribing is safe to do unconditionally and permanently -- SDL simply never raises
        // SDL_TEXTINPUT while text input is stopped (see SetFocus's Start/StopTextInput calls
        // below), so this just never fires until a TextBox is actually focused.
        TextInputEXT.TextInput += OnTextInput;
    }

    /// <summary>Adds handler after every handler already registered, from its next call on -- for one that comes and goes, such as a tab's hotkeys while the tab is open.</summary>
    public void AddInputHandler(IUiInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _inputHandlers = [.. _inputHandlers, handler];
    }

    /// <summary>Removes handler; nothing happens when it isn't registered.</summary>
    public bool RemoveInputHandler(IUiInputHandler handler)
    {
        if (Array.IndexOf(_inputHandlers, handler) is not (var index and >= 0))
        {
            return false;
        }

        _inputHandlers = [.. _inputHandlers[..index], .. _inputHandlers[(index + 1)..]];
        return true;
    }

    /// <summary>Internal, not private, so tests can simulate a typed character without a real OS text-input event -- the subscribed TextInputEXT.TextInput handler in real use otherwise.</summary>
    internal void OnTextInput(char character) => _pendingTextInput.Add(character);

    /// <summary>
    /// The Button (title bar or content area) currently held down by the mouse, if any -- null
    /// the rest of the time, including once the matching release fires (regardless of where the
    /// mouse ends up). Drives Button's own pressed-bevel look via SetPressed.
    /// </summary>
    internal Button? PressedButton { get; private set; }

    /// <summary>
    /// The Button (title bar or content area) currently under the cursor, if any -- updated from
    /// the same per-frame hover hit-test UpdateCursor already performs for the OS cursor icon, so
    /// this doesn't cost a second tree walk. Drives Button's own IsHovered highlight; every Button
    /// anywhere gets this for free rather than each owner self-polling Mouse.GetState().
    /// </summary>
    private Button? _hoveredButton;

    private void SetHoveredButton(Button? button)
    {
        if (ReferenceEquals(button, _hoveredButton))
        {
            return;
        }

        if (_hoveredButton is not null)
        {
            _hoveredButton.IsHovered = false;
        }

        _hoveredButton = button;

        if (_hoveredButton is not null)
        {
            _hoveredButton.IsHovered = true;
        }
    }

    /// <summary>Moves the scrollbar hover highlight to part of element, clearing it from whichever element had it -- see _hoveredButton for the same per-frame hover source.</summary>
    private void SetHoveredScrollbar(Element? element, ScrollbarPart part)
    {
        if (!ReferenceEquals(element, _hoveredScrollbarElement))
        {
            _hoveredScrollbarElement?.SetScrollbarHoveredPart(ScrollbarPart.None);
            _hoveredScrollbarElement = element;
        }

        _hoveredScrollbarElement?.SetScrollbarHoveredPart(part);
    }

    /// <summary>The drag/resize interaction currently in progress (or ElementInteraction.NotHit if none). Move is wired to SetRelativePosition and Resize to SetBounds, both each held frame -- see ComputeResize for the resize math.</summary>
    internal ElementInteraction ActiveInteraction => _activeInteraction;

    /// <summary>The element currently holding keyboard focus, if any -- see SetFocus/RouteKeyPressesToFocusedElement/CycleFocus.</summary>
    internal Element? FocusedElement => _focusedElement;

    /// <summary>
    /// Whether a TextBox currently holds keyboard focus -- public (unlike FocusedElement) so
    /// consumers in other assemblies (a map view that must not treat typed
    /// keys as hotkeys) can check without needing the actual
    /// focused-element reference.
    /// </summary>
    public bool IsTextBoxFocused => _focusedElement is TextBox;

    /// <summary>Focuses an element from outside -- a host calls this at startup to focus its main view, since an element's own hotkeys (see RouteHotkeysToFocusedElement) only fire while it holds focus.</summary>
    public void FocusElement(Element element) => SetFocus(element);

    /// <summary>See _defaultFocusElement.</summary>
    public void SetDefaultFocusElement(Element element) => _defaultFocusElement = element;

    /// <summary>Sets the area a top-level window may be moved or resized within, for a host whose client area changed size.</summary>
    public void SetScreenSize(Vector2 screenSize) => _screenSize = screenSize;

    /// <summary>Element.RelativePosition captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. Move's per-frame SetRelativePosition is this plus DragDelta; ComputeResize uses it as the Left/Top-edge resize baseline.</summary>
    internal Vector2 DragStartRelativePosition => _dragStartRelativePosition;

    /// <summary>Element.CurrentSize captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. ComputeResize's resize baseline.</summary>
    internal Vector2 DragStartSize => _dragStartSize;

    /// <summary>Mouse movement since the drag started, recomputed every held frame -- zero on the press frame itself and once released.</summary>
    internal Vector2 DragDelta { get; private set; }

    /// <summary>The last cursor UpdateCursor set (or the initial Arrow default, if it's never had reason to change) -- lets tests assert on cursor selection without depending on real OS cursor state.</summary>
    internal MouseCursor CurrentCursor { get; private set; } = MouseCursor.Arrow;

    /// <summary>What the mouse was over at _hoveredInteractionPosition -- see RefreshHoveredInteraction. Read by UpdateCursor and handed to the handlers, so nothing else hit-tests the cursor's position each frame.</summary>
    private ElementInteraction _hoveredInteraction = ElementInteraction.NotHit;

    /// <summary>The position _hoveredInteraction was hit-tested at; null before the first, and after anything that may have changed what's under the cursor (see InvalidateHoveredInteraction).</summary>
    private Point? _hoveredInteractionPosition;

    /// <summary>UiLayerStack.Version when _hoveredInteraction was hit-tested -- a window opened, closed or raised under a still mouse changes it.</summary>
    private int _hoveredInteractionLayersVersion;

    /// <summary>Whether the drag ghost should draw right now -- true once a content-drag payload has been held for ContentDragGhostDelayFrames, so a plain press-then-release on a drag source never flashes one.</summary>
    private bool ContentDragGhostVisible => _contentDrag is not null && _contentDragHeldFrames >= ContentDragGhostDelayFrames;

    /// <summary>See _contentDragSourceSize's own doc comment.</summary>
    internal Vector2 ContentDragSourceSize => _contentDragSourceSize;

    /// <summary>Current mouse screen position, refreshed at the top of every Update call and published through PointerState at its end.</summary>
    private Point CurrentMousePosition { get; set; }

    public void Update() => Update(Keyboard.GetState(), Mouse.GetState());

    /// <summary>
    /// Takes explicit states rather than reading Keyboard.GetState()/Mouse.GetState() itself,
    /// so tests can drive synthetic press/release/move sequences -- the public
    /// Update(GameTime) overload above is the only real caller otherwise.
    /// </summary>
    internal void Update(KeyboardState keyboardState, MouseState mouseState)
    {
        CurrentMousePosition = new Point(mouseState.X, mouseState.Y);

        _frameCounter++;
        FlushExpiredPendingClicks();

        var dropdownTookKey = RouteNavigationKeysToOpenDropdown(keyboardState);
        if (!dropdownTookKey)
        {
            RouteHotkeysToFocusedElement(keyboardState);
        }

        HandleFocusCycling(keyboardState);
        HandleEscape(keyboardState);
        RouteKeysToInputHandlers(keyboardState);
        if (!dropdownTookKey)
        {
            RouteKeyPressesToFocusedElement(keyboardState);
        }
        RouteTextInputToFocusedElement();

        if (keyboardState != _previousKeyboardState)
        {
            InvalidateHoveredInteraction();
        }

        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
        {
            HandleMousePress(mouseState);
        }
        else if (mouseState.LeftButton == ButtonState.Released && _previousMouseState.LeftButton == ButtonState.Pressed)
        {
            HandleMouseRelease(mouseState);
        }
        else if (mouseState.LeftButton == ButtonState.Pressed && _activeInteraction.Kind != ElementDragInteractionKind.None)
        {
            HandleMouseDrag(mouseState);
        }
        else if (mouseState.LeftButton == ButtonState.Pressed && _textSelectionDragBox is not null)
        {
            HandleTextSelectionDrag(mouseState);
        }

        if (mouseState.RightButton == ButtonState.Pressed && _previousMouseState.RightButton == ButtonState.Released)
        {
            HandleRightDragStart(mouseState);
        }
        else if (mouseState.RightButton == ButtonState.Released && _previousMouseState.RightButton == ButtonState.Pressed)
        {
            HandleRightDragEnd(new Point(mouseState.X, mouseState.Y));
        }
        else if (mouseState.RightButton == ButtonState.Pressed && _rightDragElement is not null)
        {
            HandleRightDrag(mouseState);
        }

        UpdateMouseWheelScroll(keyboardState, mouseState);
        UpdateCursor(mouseState);
        NotifyInputHandlersOfPointer();

        if (_contentDrag is { } contentDrag)
        {
            _contentDragHeldFrames++;

            // Announced with the ghost, not on press: a plain press-then-release on a slot isn't a drag, and
            // announcing on press would flash every drop target for the frames before the release.
            if (_contentDragHeldFrames == ContentDragGhostDelayFrames)
            {
                foreach (var handler in _contentDropHandlers)
                {
                    handler.OnDragGhostShown(contentDrag.Payload);
                }
            }
        }

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;

        _pointerState.CursorPosition = CurrentMousePosition;
        _pointerState.ContentDrag = new ContentDragState(ContentDragGhostVisible, _contentDrag?.Payload, _contentDragSourceSize, CurrentMousePosition);
    }

    /// <summary>Up, Down and Enter pressed this frame go to an open dropdown before the focused element sees them -- a TextBox under a DropdownField's list must not also move its caret. Returns whether one did.</summary>
    private bool RouteNavigationKeysToOpenDropdown(KeyboardState keyboardState)
    {
        if (_dropdownController is not { IsOpen: true } dropdownController)
        {
            return false;
        }

        var tookKey = false;
        foreach (var key in DropdownNavigationKeys)
        {
            if (IsKeyPressed(keyboardState, key) && dropdownController.HandleNavigationKey(key))
            {
                tookKey = true;
            }
        }

        return tookKey;
    }

    private static readonly Keys[] DropdownNavigationKeys = [Keys.Up, Keys.Down, Keys.Enter];

    /// <summary>
    /// Routes the whole keyboard state to whichever element is focused, once per frame (see
    /// Window.HandleHotkeys) -- a map view's scroll and zoom keys, or a TextBox's
    /// arrow-key/Backspace/copy-paste handling (see TextBox.OnHotkeysAction's own doc comment --
    /// those route through HandleHotkeys, not the discrete key-press path, for held-key repeat).
    /// UiInputController itself knows nothing about what any element's hotkeys are; it only
    /// knows which element is focused. While menu mode is active, this is scoped by
    /// IsReachableDuringMenuMode the same way TryHitTestInteraction already scopes mouse
    /// hit-testing -- routed if the focused element is actually part of what's currently
    /// interactive (a TextBox inside an open menu window), skipped otherwise (a view still focused
    /// from before a menu window opened must not have its hotkeys reach the dimmed world underneath).
    /// </summary>
    private void RouteHotkeysToFocusedElement(KeyboardState keyboardState)
    {
        if (_focusedElement is not { } focused)
        {
            return;
        }

        if (_layers.IsMenuModeActive && !IsReachableDuringMenuMode(focused))
        {
            return;
        }

        focused.HandleHotkeys(keyboardState, _previousKeyboardState);
    }

    /// <summary>Whether element -- via its root ancestor, the granularity UiLayerStack's own menu-window/exempt registries track -- is part of what's currently interactive while menu mode is active: an open menu window, a menu-mode-exempt element, or already in the always-reachable Tooltip/User tier. Shared by TryHitTestInteraction (mouse) and RouteHotkeysToFocusedElement (keyboard) so the two can't drift out of sync about what "reachable during menu mode" means.</summary>
    private bool IsReachableDuringMenuMode(Element element)
    {
        var root = GetRootAncestor(element);
        return _layers.IsMenuWindow(root) || _layers.IsMenuModeExempt(root) || _layers.LayerOf(root) is UiLayer.Tooltip or UiLayer.User;
    }

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

    /// <summary>Every input handler's own keys, each frame -- unconditional and edge-triggered like Tab and Escape (no menu-mode gating), told whether a TextBox has focus so typed characters stay typed text.</summary>
    private void RouteKeysToInputHandlers(KeyboardState keyboardState)
    {
        foreach (var handler in _inputHandlers)
        {
            handler.HandleKeys(keyboardState, _previousKeyboardState, IsTextBoxFocused);
        }
    }

    /// <summary>
    /// Per-UiLayer Escape-key behavior (see HandleEscape below and GetEscapeBehavior) -- kept as
    /// an explicit switch over every named UiLayer member, with a throwing default arm (same
    /// convention as Element.Measure's DisplayMode switch) rather than an implicit "unlisted
    /// layer does nothing," specifically so adding a new UiLayer member without updating this one
    /// fails loudly instead of silently opting that layer out of Escape entirely. [Flags] since
    /// nothing rules out a future layer wanting more than one behavior, even though today's five
    /// each use at most one.
    /// </summary>
    [Flags]
    private enum EscapeBehavior
    {
        /// <summary>Does nothing on Escape -- Tooltip (auto-hides on its own hover-tracking, was never "opened" the way a window is) and User (drag feedback, not a dismissible window at all) both use this.</summary>
        None = 0,

        /// <summary>Element.HandleEscape() (see Element.OnEscapeAction) is called on every element in this layer, unconditionally -- for layers whose own elements need to react to Escape themselves regardless of focus (a view cancelling whatever is armed).</summary>
        BroadcastToElements = 1 << 0,

        /// <summary>A tap closes the topmost (drawn-on-top) CanUserClose window in this layer; a held Escape closes every CanUserClose window in this layer at once -- for layers of dismissible popup windows.</summary>
        CloseableWindows = 1 << 1,
    }

    private static EscapeBehavior GetEscapeBehavior(UiLayer layer) => layer switch
    {
        UiLayer.Base => EscapeBehavior.BroadcastToElements,
        UiLayer.StaticHud => EscapeBehavior.BroadcastToElements,
        UiLayer.DynamicHud => EscapeBehavior.CloseableWindows,
        UiLayer.Tooltip => EscapeBehavior.None,
        UiLayer.User => EscapeBehavior.None,
        // Dismissed by its own dedicated check at the very top of HandleEscape instead, not this
        // generic per-layer mechanism -- it must take priority over an already-open menu window's
        // own TopmostMenuWindow short-circuit (see HandleEscape), which CloseableWindows here
        // would never actually reach past.
        UiLayer.ContextMenu => EscapeBehavior.None,
        _ => throw new NotImplementedException($"No Escape behavior defined for UiLayer.{layer}."),
    };

    /// <summary>
    /// Escape must stay unconditional too, the same reasoning as Tab above: broadcast to every
    /// BroadcastToElements-layer element (not just whichever holds focus) rather than routed only
    /// to the focused one, since a view with something armed shouldn't have to hold
    /// keyboard focus for Escape to cancel it. No-op by default (Element.OnEscapeAction).
    ///
    /// A fresh press (the first held frame) also closes the frontmost closeable window across
    /// every CloseableWindows-layer -- one at a time, same as
    /// clicking its own close button would (see CloseTopmostClosableWindow). Continuing to hold
    /// Escape past EscapeHoldCloseAllFrames instead sweeps every closeable window closed at once,
    /// unconditionally across every layer, via UiLayerStack.CloseAllClosableWindows -- the same
    /// sweep a host uses for any other "clear everything" trigger -- so a player buried under several popups can clear them all without repeated
    /// presses.
    /// </summary>
    private void HandleEscape(KeyboardState keyboardState)
    {
        if (!keyboardState.IsKeyDown(Keys.Escape))
        {
            _escapeHeldFrames = 0;
            _escapeHoldCloseAllFired = false;
            return;
        }

        _escapeHeldFrames++;

        if (_escapeHeldFrames == 1)
        {
            // An open context menu absorbs Escape entirely, before anything else below -- it
            // must take priority even over an already-open menu window's own TopmostMenuWindow
            // short-circuit (CloseTopmostClosableWindow), which would otherwise close the
            // whole menu window instead of just dismissing a context menu opened from
            // something inside it.
            if (_dropdownController is { IsOpen: true } dropdownController)
            {
                dropdownController.Close();
                return;
            }

            // While menu mode is active, Escape must not reach through it to Base/StaticHud
            // elements behind it (a view cancelling whatever is armed) -- that's exactly
            // the input TryHitTestInteraction already refuses to route to those elements while
            // menu mode is active, and broadcasting Escape to them here would be the same leak
            // through a different door.
            if (!_layers.IsMenuModeActive)
            {
                foreach (var layer in UiLayerStack.LayersAscending())
                {
                    if ((GetEscapeBehavior(layer) & EscapeBehavior.BroadcastToElements) == 0)
                    {
                        continue;
                    }

                    foreach (var element in _layers[layer])
                    {
                        element.HandleEscape();
                    }
                }
            }

            CloseTopmostClosableWindow();
            return;
        }

        if (!_escapeHoldCloseAllFired && _escapeHeldFrames >= EscapeHoldCloseAllFrames)
        {
            _escapeHoldCloseAllFired = true;
            _layers.CloseAllClosableWindows();
        }
    }

    /// <summary>
    /// Closes just the frontmost (highest CloseableWindows layer, then last-raised/drawn-on-top
    /// within it) closeable window -- a Window with CanUserClose true. Deliberately excludes
    /// non-Window elements (folder icons, plain Element subclasses)
    /// and Windows with CanUserClose false (a pinned summary popup) -- neither is something a
    /// player "closes," they're persistent HUD chrome. A no-op if nothing closeable is currently
    /// open anywhere.
    /// </summary>
    private void CloseTopmostClosableWindow()
    {
        // While menu mode is active, Escape can only ever dismiss the frontmost menu window --
        // never reach past it to some other, unrelated closeable window sitting behind it in the
        // same tier.
        if (_layers.TopmostMenuWindow is { } menuWindow)
        {
            if (menuWindow is Window { CanUserClose: true } closeableMenuWindow)
            {
                closeableMenuWindow.Close();
            }

            return;
        }

        foreach (var layer in UiLayerStack.LayersDescending())
        {
            if ((GetEscapeBehavior(layer) & EscapeBehavior.CloseableWindows) == 0)
            {
                continue;
            }

            var elements = _layers[layer];
            for (var index = elements.Count - 1; index >= 0; index--)
            {
                if (elements[index] is Window { CanUserClose: true } window)
                {
                    window.Close();
                    return;
                }
            }
        }
    }

    private void HandleMousePress(MouseState mouseState)
    {
        var clickPosition = new Point(mouseState.X, mouseState.Y);

        // An open context menu absorbs a click that lands outside it entirely -- one input,
        // consumed by the cancel --
        // rather than also raising/focusing/pressing whatever happens to be underneath. A click
        // that lands ON the menu (e.g. a row Button) falls through to the ordinary hit-test below
        // exactly as normal, since Contains is false only for a genuinely outside click.
        if (_dropdownController is { IsOpen: true } dropdownController && !dropdownController.Menu.Rectangle.Contains(clickPosition) && dropdownController.Owner?.Rectangle.Contains(clickPosition) != true)
        {
            dropdownController.Close();
            _activeInteraction = ElementInteraction.NotHit;
            return;
        }

        // Same "one input consumed by the cancel" spirit as the context-menu check above -- an input
        // handler that consumes the press (closing a window the click landed outside of, say) keeps
        // it from also reaching whatever's underneath.
        foreach (var handler in _inputHandlers)
        {
            if (handler.TryConsumePress(clickPosition))
            {
                _activeInteraction = ElementInteraction.NotHit;
                return;
            }
        }

        InvalidateHoveredInteraction();
        _activeInteraction = HoveredInteractionAt(clickPosition);
        PressedButton = _activeInteraction.Button;
        PressedButton?.SetPressed(true);
        DragDelta = Vector2.Zero;

        _textSelectionDragBox = _activeInteraction.Kind == ElementDragInteractionKind.None ? _activeInteraction.Element as TextBox : null;
        _textSelectionDragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
        _textSelectionDragExceededTapThreshold = false;

        if (_activeInteraction.Element is not null)
        {
            RaiseToFront(_activeInteraction.Element);

            if (_activeInteraction.Kind == ElementDragInteractionKind.None)
            {
                // A button click is a momentary trigger, never a focus target (see Button's own
                // CanUserFocus -- always false) -- skip SetFocus entirely rather than falling
                // through to _activeInteraction.Element, which for a header button is the owning
                // window (so RaiseToFront above still raises it), not the button. SetFocus on
                // that window would otherwise redirect via NextFocusableDescendant into whatever
                // unrelated child happens to be focusable -- clicking a window's
                // Close button would focus its search box.
                if (_activeInteraction.Button is null)
                {
                    SetFocus(_activeInteraction.Element.CanUserFocus ? _activeInteraction.Element : null);
                }
            }
            else
            {
                _dragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
                _dragStartRelativePosition = _activeInteraction.Element.RelativePosition;
                _dragStartSize = _activeInteraction.Element.CurrentSize;
                _dragStartScrollOffset = _activeInteraction.Element.ScrollOffset;

                if (_activeInteraction.ScrollbarPart != ScrollbarPart.None)
                {
                    _activeInteraction.Element.SetScrollbarPressedPart(_activeInteraction.ScrollbarPart);
                }

                if (_activeInteraction.Kind == ElementDragInteractionKind.ScrollTrack)
                {
                    _scrollTrackHeldFrames = 0;
                    _scrollTrackPageDirection = _activeInteraction.Element.GetScrollbarPageDirectionToward(_activeInteraction.ScrollbarPart, clickPosition);
                    _activeInteraction.Element.PageScrollbar(_activeInteraction.ScrollbarPart, _scrollTrackPageDirection);
                }
            }
        }
        else
        {
            SetFocus(null);
        }

        TryStartContentDrag(clickPosition);

        foreach (var handler in _inputHandlers)
        {
            handler.OnPress(_activeInteraction.Element, clickPosition);
        }
    }

    /// <summary>
    /// Captures a content-drag payload if the press landed on a drag source -- the pressed element itself, or the pressed
    /// window's content (an icon bar answers for its own slots) -- unless a drop handler refuses it. Independent of
    /// _activeInteraction.Kind: a plain content click on a source resolves to Kind None, so this runs on every press rather
    /// than being folded into the Kind-gated branch above.
    /// </summary>
    private void TryStartContentDrag(Point clickPosition)
    {
        _contentDrag = null;
        _contentDragHeldFrames = 0;

        var source = _activeInteraction.Element as IContentDragSource ?? (_activeInteraction.Element as Window)?.Content as IContentDragSource;
        if (source is null || !source.TryBeginContentDrag(clickPosition, out var start))
        {
            return;
        }

        foreach (var handler in _contentDropHandlers)
        {
            if (!handler.AllowsDragStart(start.Payload))
            {
                return;
            }
        }

        _contentDrag = start;
        _contentDragSourceSize = start.SourceSize;
        _contentDragStartMousePosition = new Vector2(clickPosition.X, clickPosition.Y);
    }

    /// <summary>Tells every input handler what the pointer is over and where, once per frame after the cursor is chosen, and whether a window move, resize or scrollbar drag is under way (the hovered element isn't refreshed during one).</summary>
    private void NotifyInputHandlersOfPointer()
    {
        var isInteractionInProgress = _activeInteraction.Kind != ElementDragInteractionKind.None;
        foreach (var handler in _inputHandlers)
        {
            handler.OnPointerUpdate(_hoveredInteraction.Element, CurrentMousePosition, isInteractionInProgress);
        }
    }

    /// <summary>
    /// Fires on release, not press -- standard button convention (press only starts the pressed
    /// visual; release commits, re-hit-testing the same element at the release position so a
    /// button/title/content click that's been dragged off its target quietly does nothing rather
    /// than firing against whatever else the mouse happens to be over). This is also the only way
    /// the pressed visual is ever actually observable: firing on press meant a destructive action
    /// (Close) usually destroyed the button before a held frame could even render.
    /// </summary>
    private void HandleMouseRelease(MouseState mouseState)
    {
        // A completed text-selection drag (see HandleTextSelectionDrag) or a completed content
        // drag (see ExceededContentDragTapThreshold) both skip the ordinary content-click --
        // otherwise a cell/slot dragged and dropped elsewhere would ALSO fire its own origin
        // Clicked handler on release purely because _activeInteraction.Element still
        // refers to the pressed cell, never updated for wherever the drag actually landed. ResolveContentDrag below makes this exact same tap-vs-drag distinction
        // independently for its own purposes; this just applies it one step earlier, before the
        // click fires at all, rather than after the fact.
        // A scrollbar press is chrome, not content -- releasing it never clicks the scrolled element.
        var isScrollbarInteraction = _activeInteraction.ScrollbarPart != ScrollbarPart.None;
        var isPointerDrag = _activeInteraction.Kind == ElementDragInteractionKind.PointerDrag;
        if (isPointerDrag)
        {
            _activeInteraction.Element?.EndPointerDrag();
        }

        if (!isScrollbarInteraction && !isPointerDrag && !_textSelectionDragExceededTapThreshold && !ExceededContentDragTapThreshold(new Point(mouseState.X, mouseState.Y)))
        {
            DispatchClick(_activeInteraction.Element, new Point(mouseState.X, mouseState.Y));
        }

        if (isScrollbarInteraction)
        {
            _activeInteraction.Element?.SetScrollbarPressedPart(ScrollbarPart.None);
        }

        // Hit-tested once, after the click (which may have opened or closed something under the cursor), and shared by
        // the drop and every handler -- and by this frame's cursor, through the hover cache.
        var releasePosition = new Point(mouseState.X, mouseState.Y);
        InvalidateHoveredInteraction();
        var releasedElement = HoveredInteractionAt(releasePosition).Element;

        var wasDropped = ResolveContentDrag(releasedElement, releasePosition);

        foreach (var handler in _inputHandlers)
        {
            handler.OnRelease(releasedElement, releasePosition);
        }

        // A drop moves items between windows, so what's under the cursor may have changed again.
        if (wasDropped)
        {
            InvalidateHoveredInteraction();
        }

        PressedButton?.SetPressed(false);
        PressedButton = null;
        _activeInteraction = ElementInteraction.NotHit;
        DragDelta = Vector2.Zero;
        _textSelectionDragBox = null;
        _textSelectionDragExceededTapThreshold = false;
    }

    /// <summary>
    /// The one dispatch point for every content click. An element nobody has ever
    /// subscribed DoubleClicked on (the overwhelming majority -- every plain Button, TabbedContent's
    /// tab tiles, ...) fires Clicked immediately via the ordinary HandleClick path -- zero added latency. An element that does
    /// want double-click detection is routed through the deferred single/double-click state machine
    /// instead -- see HandleDoubleClickAwareClick.
    /// </summary>
    private void DispatchClick(Element? element, Point mousePosition)
    {
        if (element is null)
        {
            return;
        }

        if (element.WantsDoubleClickDetection)
        {
            HandleDoubleClickAwareClick(element);
        }
        else
        {
            element.HandleClick(mousePosition);
        }
    }

    /// <summary>
    /// A click on an element that opted into double-click detection is never actioned immediately --
    /// it's held in _pendingSingleClickFrameByElement until either DoubleClickWindowFrames passes
    /// with nothing else claiming it (flushed from FlushExpiredPendingClicks, firing Clicked as if it
    /// had happened normally), or a second click on this same element arrives within the window
    /// (cancels the pending single-click outright and fires DoubleClicked instead, so whatever
    /// Clicked would have done never runs at all for a genuine double-click).
    /// </summary>
    private void HandleDoubleClickAwareClick(Element element)
    {
        if (_pendingSingleClickFrameByElement.TryGetValue(element, out var pendingFrame) &&
            _frameCounter - pendingFrame <= DoubleClickWindowFrames)
        {
            CancelPendingSingleClick(element);
            element.RaiseDoubleClicked();
            return;
        }

        _pendingSingleClickFrameByElement[element] = _frameCounter;
        element.Closed += OnPendingClickElementClosed;
    }

    /// <summary>Fires whatever single click is still pending once its own window has fully elapsed with no second click -- called once per Update. Two-pass (collect then fire) since firing Clicked can trigger arbitrary consumer code that might itself click something else, and mutating _pendingSingleClickFrameByElement mid-enumeration would throw.</summary>
    private void FlushExpiredPendingClicks()
    {
        if (_pendingSingleClickFrameByElement.Count == 0)
        {
            return;
        }

        List<Element>? expired = null;
        foreach (var (pendingElement, pendingFrame) in _pendingSingleClickFrameByElement)
        {
            if (_frameCounter - pendingFrame > DoubleClickWindowFrames)
            {
                (expired ??= []).Add(pendingElement);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var pendingElement in expired)
        {
            CancelPendingSingleClick(pendingElement);
            pendingElement.RaiseClicked();
        }
    }

    /// <summary>An element closed/returned to the pool (a grid recreating its cells) while its click was still pending must never later fire against whatever the pooled instance gets reused for -- evicts it the moment that happens, same as the double-click-escalation and timeout-flush paths already do on their own way out.</summary>
    private void OnPendingClickElementClosed(Element element) => CancelPendingSingleClick(element);

    private void CancelPendingSingleClick(Element element)
    {
        if (_pendingSingleClickFrameByElement.Remove(element))
        {
            element.Closed -= OnPendingClickElementClosed;
        }
    }

    /// <summary>
    /// True while a content-drag payload is captured (see TryStartContentDrag) AND the mouse has
    /// moved at least ContentDragTapThresholdPixels since the press that captured it -- the same
    /// tap-vs-drag distinction ResolveContentDrag makes for its own purposes, exposed separately so
    /// HandleMouseRelease can skip the ordinary content-click for a release this method would
    /// itself go on to treat as a genuine drop, not a plain click.
    /// </summary>
    private bool ExceededContentDragTapThreshold(Point releasePosition)
    {
        if (_contentDrag is null)
        {
            return false;
        }

        var releaseVector = new Vector2(releasePosition.X, releasePosition.Y);
        return Vector2.Distance(_contentDragStartMousePosition, releaseVector) >= ContentDragTapThresholdPixels;
    }

    /// <summary>
    /// Resolves an in-progress content drag against the release position -- a no-op if the mouse never moved past
    /// ContentDragTapThresholdPixels (a plain click on a source must not drop it where it already is). This method's job is
    /// gesture recognition and hit-testing the drop target; what the drop means is the drop handlers', tried in order until
    /// one claims it. Always ends the drag, dropped or not, so a missed drag never leaves a drop target glowing or a stale
    /// payload for the next press to inherit. True if a drag was resolved against dropTarget, whether or not a handler
    /// claimed it.
    /// </summary>
    private bool ResolveContentDrag(Element? dropTarget, Point releasePosition)
    {
        if (_contentDrag is not { } contentDrag)
        {
            return false;
        }

        try
        {
            if (!ExceededContentDragTapThreshold(releasePosition))
            {
                return false;
            }

            foreach (var handler in _contentDropHandlers)
            {
                if (handler.TryDrop(contentDrag.Payload, dropTarget, releasePosition))
                {
                    break;
                }
            }

            return true;
        }
        finally
        {
            _contentDrag = null;
            _contentDragHeldFrames = 0;

            foreach (var handler in _contentDropHandlers)
            {
                handler.OnDragEnded(contentDrag.Payload);
            }
        }
    }

    private void HandleMouseDrag(MouseState mouseState)
    {
        DragDelta = new Vector2(mouseState.X, mouseState.Y) - _dragStartMousePosition;

        if (_activeInteraction.Kind == ElementDragInteractionKind.Move && _activeInteraction.Element is not null)
        {
            var element = _activeInteraction.Element;
            var desiredPosition = _dragStartRelativePosition + DragDelta;
            element.SetRelativePosition(ClampMoveToBounds(desiredPosition, element.CurrentSize, GetPositionBounds(element)));
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.Resize && _activeInteraction.Element is not null)
        {
            var element = _activeInteraction.Element;
            var (relativePosition, size) = ComputeResize(element, _activeInteraction.Edges, _dragStartRelativePosition, _dragStartSize, DragDelta);
            (relativePosition, size) = ClampResizeToBounds(relativePosition, size, GetPositionBounds(element), element.MinimumSize);
            element.SetBounds(relativePosition, size);
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.PointerDrag && _activeInteraction.Element is not null)
        {
            _activeInteraction.Element.DragPointer(DragDelta);
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.ScrollThumb && _activeInteraction.Element is not null)
        {
            _activeInteraction.Element.DragScrollbarThumb(_activeInteraction.ScrollbarPart, _dragStartScrollOffset, DragDelta);
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.ScrollTrack && _activeInteraction.Element is not null)
        {
            // Only ever in the press's own direction: a page that rounds the thumb a pixel past the
            // cursor must stop there, not page back the other way.
            _scrollTrackHeldFrames++;
            var element = _activeInteraction.Element;
            var part = _activeInteraction.ScrollbarPart;
            if (IsScrollTrackRepeatFrame(_scrollTrackHeldFrames) &&
                _scrollTrackPageDirection != 0 &&
                element.GetScrollbarPageDirectionToward(part, new Point(mouseState.X, mouseState.Y)) == _scrollTrackPageDirection)
            {
                element.PageScrollbar(part, _scrollTrackPageDirection);
            }
        }
    }

    private static bool IsScrollTrackRepeatFrame(int heldFrames) =>
        heldFrames >= ScrollTrackRepeatInitialDelayFrames &&
        (heldFrames - ScrollTrackRepeatInitialDelayFrames) % ScrollTrackRepeatIntervalFrames == 0;

    /// <summary>
    /// Extends a TextBox's selection while a left-button press on it is held and dragged --
    /// mirrors HandleRightDrag's own tap-vs-drag distinction (RightClickTapThresholdPixels), just
    /// gating BeginSelectionDrag instead of forwarding every frame from the start: a plain click
    /// (never exceeding the threshold) never touches selection state through this path at all,
    /// only via the ordinary OnContentClickAction at release. BeginSelectionDrag fires exactly
    /// once, the frame the threshold is first exceeded, anchoring the selection at the original
    /// press position rather than wherever the drag happened to cross the threshold.
    /// </summary>
    private void HandleTextSelectionDrag(MouseState mouseState)
    {
        var currentPosition = new Vector2(mouseState.X, mouseState.Y);

        if (!_textSelectionDragExceededTapThreshold)
        {
            if (Vector2.Distance(_textSelectionDragStartMousePosition, currentPosition) < TextSelectionDragTapThresholdPixels)
            {
                return;
            }

            _textSelectionDragExceededTapThreshold = true;
            _textSelectionDragBox?.BeginSelectionDrag(new Point((int)_textSelectionDragStartMousePosition.X, (int)_textSelectionDragStartMousePosition.Y));
        }

        _textSelectionDragBox?.ExtendSelectionDrag(new Point((int)currentPosition.X, (int)currentPosition.Y));
    }

    /// <summary>
    /// Captures which element a right-mouse-button drag started over, hit-testing the same way
    /// a left-click does (TryHitTestInteraction) -- but with no raise-to-front/focus side
    /// effects, since a drag-to-pan gesture shouldn't steal focus or reorder elements the way
    /// clicking one does. Null (nothing hit, e.g. empty space between windows) means the drag
    /// simply forwards to nothing until released. Also snapshots the press position as the
    /// anchor HandleRightDrag measures every subsequent frame's total delta from.
    /// </summary>
    private void HandleRightDragStart(MouseState mouseState)
    {
        var position = new Point(mouseState.X, mouseState.Y);

        // Every right-press dismisses an already-open context menu unconditionally, whether or
        // not this gesture goes on to open a fresh one -- an element's right-click tap (on
        // release, if this turns out to be a tap rather than a drag) opens a fresh one if it has a menu.
        _dropdownController?.Close();

        // Every input handler hears every right press too, with the same unconditional precedence.
        foreach (var handler in _inputHandlers)
        {
            handler.OnRightPress(position);
        }

        InvalidateHoveredInteraction();
        _rightDragElement = HoveredInteractionAt(position).Element;
        _rightDragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
        _rightDragExceededTapThreshold = false;
        _rightDragElement?.HandleRightDragStart();
    }

    /// <summary>Forwards the total mouse-pixel delta since the drag started (not this frame's increment) to whichever element the drag started over -- see Window.HandleRightDrag. Also latches _rightDragExceededTapThreshold once this gesture has moved enough to count as a real drag, not a tap -- see HandleRightDragEnd.</summary>
    private void HandleRightDrag(MouseState mouseState)
    {
        var totalDelta = new Vector2(mouseState.X, mouseState.Y) - _rightDragStartMousePosition;

        if (totalDelta.Length() > RightClickTapThresholdPixels)
        {
            _rightDragExceededTapThreshold = true;
        }

        _rightDragElement?.HandleRightDrag(totalDelta);
    }

    /// <summary>A gesture that never exceeded the tap threshold reads as a right-click tap (a cancel, say) instead of a drag-end -- a real drag's own end-of-gesture handling (e.g. MapWindow settling its smooth scroll onto the tile grid) has nothing to do for a tap anyway, since it never moved.</summary>
    private void HandleRightDragEnd(Point releasePosition)
    {
        if (_rightDragExceededTapThreshold)
        {
            _rightDragElement?.HandleRightDragEnd();
        }
        else
        {
            _rightDragElement?.HandleRightClickTap(releasePosition);
        }

        _rightDragElement = null;
        InvalidateHoveredInteraction();
    }

    /// <summary>
    /// Scrolls whichever element under the cursor opts into
    /// CanUserScrollVertical/Horizontal (see Window.ScrollBy) -- if the element directly under
    /// the cursor can't scroll itself, walks up ParentElement to the nearest ancestor that can
    /// (e.g. hovering a non-scrollable inspector component box inside a scrollable inspection
    /// container scrolls the container), the same walk-up shape GetRootAncestor already uses,
    /// just stopping early at the first scrollable ancestor instead of going all the way to
    /// root. A chain with no scrollable element anywhere (the pre-existing behavior for a lone
    /// non-scrollable window) is still a no-op. Independent of ActiveInteraction, so scrolling
    /// one element mid-drag of another is harmless rather than something that needs guarding
    /// against. ScrollWheelValue is cumulative, not per-frame, so this reads like every other
    /// per-frame delta here (see the mouse-button handling above): diffed against last frame's
    /// value.
    /// </summary>
    /// <remarks>
    /// Scrolls horizontally instead when the element can only scroll horizontally, when Shift is
    /// held, or when the cursor is on its horizontal scrollbar -- each only if the element allows
    /// horizontal scrolling at all; otherwise the wheel stays vertical.
    /// </remarks>
    private void UpdateMouseWheelScroll(KeyboardState keyboardState, MouseState mouseState)
    {
        var wheelDelta = mouseState.ScrollWheelValue - _previousMouseState.ScrollWheelValue;
        if (wheelDelta == 0)
        {
            return;
        }

        var position = new Point(mouseState.X, mouseState.Y);
        var hoveredInteraction = HoveredInteractionAt(position);
        if (hoveredInteraction.Element is not { } element)
        {
            return;
        }

        if (OfferMouseWheel(element, WholeNotches(wheelDelta)))
        {
            InvalidateHoveredInteraction();
            return;
        }

        if (FindScrollableAncestor(element) is not { } scrollableElement)
        {
            return;
        }

        // The content moves under the cursor.
        InvalidateHoveredInteraction();

        var scrollDelta = -wheelDelta / WheelNotchValue * ScrollPixelsPerNotch;
        var isShiftDown = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
        var isOverHorizontalScrollbar = ReferenceEquals(hoveredInteraction.Element, scrollableElement) &&
            hoveredInteraction.ScrollbarPart is ScrollbarPart.HorizontalTrack or ScrollbarPart.HorizontalThumb;
        var scrollsHorizontally = scrollableElement.CanUserScrollHorizontal &&
            (!scrollableElement.CanUserScrollVertical || isShiftDown || isOverHorizontalScrollbar);
        scrollableElement.ScrollBy(scrollsHorizontally ? new Vector2(scrollDelta, 0) : new Vector2(0, scrollDelta));
    }

    /// <summary>A wheel movement as whole notches -- at least one for any movement, so a fine-grained wheel that reports a fraction of a notch still steps a value.</summary>
    private static int WholeNotches(int wheelDelta) => (int)(wheelDelta / WheelNotchValue) is var notches and not 0 ? notches : System.Math.Sign(wheelDelta);

    /// <summary>Offers the wheel to element and each ancestor in turn up to the first scrollable one, which scrolls if none of them uses it -- so a number field inside a scrolling list steps its value, and the list scrolls everywhere else.</summary>
    private static bool OfferMouseWheel(Element element, int notches)
    {
        if (notches == 0)
        {
            return false;
        }

        for (var candidate = (Element?)element; candidate is not null; candidate = candidate.ParentElement)
        {
            if (candidate.HandleMouseWheel(notches))
            {
                return true;
            }

            if (candidate.CanUserScrollVertical || candidate.CanUserScrollHorizontal)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Starts at element itself (so an already-scrollable hit is unchanged) and walks ParentElement upward, returning the first element that opts into CanUserScrollVertical/Horizontal, or null if nothing in the chain does.</summary>
    private static Element? FindScrollableAncestor(Element element)
    {
        for (var candidate = (Element?)element; candidate is not null; candidate = candidate.ParentElement)
        {
            if (candidate.CanUserScrollVertical || candidate.CanUserScrollHorizontal)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Tooltip/User both sit structurally above DynamicHud, where every menu window lives today -- see the menu-mode branch of TryHitTestInteraction for why hit-testing still reaches them while menu mode is active. Dropdown is deliberately not in this array -- see TryHitTestInteraction's own doc comment for why it needs checking even earlier than this.</summary>
    private static readonly UiLayer[] LayersAboveMenuMode = [UiLayer.User, UiLayer.Tooltip];

    /// <summary>User tier first, then DynamicHUD, then StaticHUD, then Base -- a higher tier can never lose to a lower one. Each tier topmost (last-raised) first. User is checked first purely for consistency (see the class's own doc comment) -- nothing placed there today is ever actually hit, since DragGhostContent's host window has no clickable content.</summary>
    private ElementInteraction TryHitTestInteraction(Point position)
    {
        if (_layers.IsMenuModeActive)
        {
            // Dropdown is checked before even IsMenuWindow -- it's always the true topmost
            // tier (see UiLayer.ContextMenu's own doc comment), but a menu opened from something
            // inside a menu window (a search box) commonly renders
            // overlapping that same menu window's own bounds. TryHitTestWhere(IsMenuWindow) below
            // would otherwise resolve a click there to the menu window's own content underneath,
            // never reaching the context menu actually drawn on top of it.
            var contextMenuInteraction = TryHitTestInList(_layers[UiLayer.ContextMenu], position);
            if (contextMenuInteraction.Element is not null)
            {
                return contextMenuInteraction;
            }

            var menuInteraction = TryHitTestWhere(position, _isMenuWindow);
            if (menuInteraction.Element is not null)
            {
                return menuInteraction;
            }

            foreach (var layer in LayersAboveMenuMode)
            {
                var interaction = TryHitTestInList(_layers[layer], position);
                if (interaction.Element is not null)
                {
                    return interaction;
                }
            }

            return TryHitTestWhere(position, _isMenuModeExempt);
        }

        foreach (var layer in UiLayerStack.LayersDescending())
        {
            var interaction = TryHitTestInList(_layers[layer], position);
            if (interaction.Element is not null)
            {
                return interaction;
            }
        }

        return ElementInteraction.NotHit;
    }

    /// <summary>Hit-tests every layer descending, topmost-drawn-first within each, but only elements matching predicate -- a non-matching sibling sharing the same layer (a folder icon beside the popup it opened, both DynamicHud) is skipped entirely, never even hit-tested into its own children. Shared by TryHitTestInteraction's two menu-mode cases: UiLayerStack.IsMenuWindow (the open menu-window set) and UiLayerStack.IsMenuModeExempt (everything opted out of menu mode's blocking without itself being a menu window).</summary>
    private ElementInteraction TryHitTestWhere(Point position, Func<Element, bool> predicate)
    {
        foreach (var layer in UiLayerStack.LayersDescending())
        {
            var elements = _layers[layer];
            for (var index = elements.Count - 1; index >= 0; index--)
            {
                if (!predicate(elements[index]))
                {
                    continue;
                }

                var interaction = elements[index].TryHitTestInteraction(position);
                if (interaction.Element is not null)
                {
                    return interaction;
                }
            }
        }

        return ElementInteraction.NotHit;
    }

    private static ElementInteraction TryHitTestInList(IReadOnlyList<Element> elements, Point position)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            var interaction = elements[index].TryHitTestInteraction(position);
            if (interaction.Element is not null)
            {
                return interaction;
            }
        }

        return ElementInteraction.NotHit;
    }

    /// <summary>
    /// Raises element within its own parent's children (no-op if it has no parent -- see
    /// Element.RaiseToFront), then raises whichever top-level ancestor contains it within its
    /// own tier (baseElements/staticHudElements/dynamicHudElements/userElements), so the whole
    /// subtree ends up drawn/hit-tested on top of its siblings at every level. Also marks that
    /// root ancestor as the active window (see SetActiveWindow) -- this runs on every click that
    /// hits anything at all, regardless of whether SetFocus goes on to change keyboard focus.
    /// </summary>
    private void RaiseToFront(Element element)
    {
        element.RaiseToFront();

        var rootAncestor = GetRootAncestor(element);
        _layers.RaiseToFront(rootAncestor);
        SetActiveWindow(rootAncestor);
    }

    /// <summary>
    /// The root-ancestor element most recently raised to front -- see Element.IsActiveWindow.
    /// Tracked independently of _focusedElement/SetFocus: a click on a non-focusable row/cell/
    /// column (most menu-window content) still raises its window to front here even though
    /// SetFocus clears keyboard focus to null for that same click.
    /// </summary>
    private Element? _activeWindow;

    private void SetActiveWindow(Element window)
    {
        if (_activeWindow == window)
        {
            return;
        }

        if (_activeWindow is not null)
        {
            _activeWindow.SetIsActiveWindow(false);
            _activeWindow.Closed -= OnActiveWindowClosed;
        }

        _activeWindow = window;
        _activeWindow.SetIsActiveWindow(true);
        _activeWindow.Closed += OnActiveWindowClosed;
    }

    /// <summary>Clears _activeWindow when it closes -- otherwise a stale reference to a since-pooled-and-reused Element could compare equal to a later SetActiveWindow(sameInstance) call and wrongly skip re-marking it active (see SetActiveWindow's reference-equality check).</summary>
    private void OnActiveWindowClosed(Element closedElement)
    {
        closedElement.Closed -= OnActiveWindowClosed;

        if (_activeWindow == closedElement)
        {
            _activeWindow = null;
        }
    }

    /// <summary>Walks up ParentElement to the top-level ancestor -- shared by RaiseToFront and CycleFocus, both of which operate on whichever tier-root element a given element belongs to, not the element itself.</summary>
    private static Element GetRootAncestor(Element element)
    {
        var rootAncestor = element;
        while (rootAncestor.ParentElement is not null)
        {
            rootAncestor = rootAncestor.ParentElement;
        }

        return rootAncestor;
    }

    /// <summary>
    /// Subscribes to the new element's FocusRequested/DisplayModeChanged and its whole
    /// ancestor chain's Closed events (see _focusedElementAncestorChain) so a focused element (or
    /// an ancestor of it) closing or minimizing -- and potentially being pooled and reused for
    /// something else entirely (see WindowService) -- can't leave this holding a stale
    /// reference that treats the reused instance as focused, and so an element that can't move
    /// focus itself (e.g. a TextBox submitting via Enter) can ask to be defocused in favor of
    /// another element.
    /// </summary>
    /// <remarks>
    /// Redirects into newElement.NextFocusableDescendant(null) first, if it has any focusable
    /// (CanUserFocus) children -- a container with a focusable child is never itself the
    /// terminal focus target, its first focusable child is. For every element without a
    /// focusable child (every plain window/tile, which all explicitly opt out via
    /// CanUserFocus = false) this resolves to newElement itself, unchanged. Falls back to
    /// _defaultFocusElement when that still leaves no target at all (newElement itself null, e.g.
    /// RedirectFocusAwayFrom finding no sibling to move to).
    /// </remarks>
    private void SetFocus(Element? newElement)
    {
        var target = newElement?.NextFocusableDescendant(null) ?? newElement ?? _defaultFocusElement;

        if (_focusedElement == target)
        {
            return;
        }

        // SDL's text-input mode is meant to bracket the lifetime of whatever widget is actually
        // receiving typed characters, not run for the whole app session -- left on permanently,
        // every keystroke (movement hotkeys included) gets fed through
        // any active OS IME, popping up composition/candidate UI during ordinary gameplay, and
        // on touch/mobile SDL backends StartTextInput is also what raises the on-screen
        // keyboard. Only toggled on an actual TextBox <-> non-TextBox edge, not every focus
        // change, so tabbing between two ordinary elements doesn't touch it at all.
        if (target is TextBox && _focusedElement is not TextBox)
        {
            StartTextInput();
        }
        else if (_focusedElement is TextBox && target is not TextBox)
        {
            StopTextInput();
        }

        UnsubscribeFocusTracking();
        _focusedElement?.SetFocused(false);

        _focusedElement = target;
        _focusedElementSiblings = target is not null
            ? GetSiblingContainer(target)
            : null;

        if (_focusedElement is not null)
        {
            _focusedElement.SetFocused(true);
            _focusedElement.FocusRequested += OnFocusedElementRequestedFocus;
            _focusedElement.DisplayModeChanged += OnFocusedElementDisplayModeChanged;

            for (var ancestor = _focusedElement; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                _focusedElementAncestorChain.Add(ancestor);
                ancestor.Closed += OnFocusedElementClosed;
            }
        }
    }

    private void UnsubscribeFocusTracking()
    {
        if (_focusedElement is not null)
        {
            _focusedElement.FocusRequested -= OnFocusedElementRequestedFocus;
            _focusedElement.DisplayModeChanged -= OnFocusedElementDisplayModeChanged;
        }

        foreach (var ancestor in _focusedElementAncestorChain)
        {
            ancestor.Closed -= OnFocusedElementClosed;
        }
        _focusedElementAncestorChain.Clear();
    }

    private void OnFocusedElementRequestedFocus(Element requestedElement) => SetFocus(requestedElement);

    /// <summary>
    /// Fires for the focused element itself closing, or any of its ancestors (see
    /// _focusedElementAncestorChain) -- e.g. closing a popup while its TextBox
    /// child holds focus: the popup is what actually calls Close(), the TextBox never does, so
    /// without the whole-chain subscription this would never fire at all and focus would be
    /// left dangling on a TextBox whose window is now hidden/pooled.
    /// </summary>
    private void OnFocusedElementClosed(Element closedElement) => RedirectFocusAwayFrom();

    /// <summary>
    /// A minimized element reads as "no longer the active thing", the same as a closed one --
    /// redirect focus the same way. Fires on every DisplayModeChanged, not just transitions
    /// into Minimized (restoring back out of it, or an unrelated Fixed/Fill change, also raise
    /// this event), so only the Minimized case is treated as a redirect trigger here. A behavior that
    /// "minimizes" by closing is covered by OnFocusedElementClosed instead.
    /// </summary>
    private void OnFocusedElementDisplayModeChanged(Element element)
    {
        if (element.DisplayMode == ElementDisplayMode.Minimized)
        {
            RedirectFocusAwayFrom();
        }
    }

    /// <summary>
    /// Moves focus to a sibling of the currently focused element, rather than leaving focus on
    /// nothing, once it (or an ancestor of it) has closed or minimized. "Sibling" is scoped to
    /// groups of genuinely interchangeable elements -- other children under the same parent
    /// (a form of several TextBoxes), or other DynamicHUD popups (the next one once the topmost
    /// is dismissed) -- not the Base tier, whose windows are fixed, distinct panels rather than a
    /// stack of equivalent ones; a closing Base window falls through to _defaultFocusElement
    /// instead of grabbing some unrelated Base panel. Uses _focusedElementSiblings (snapshotted when this element gained focus, see
    /// SetFocus) rather than re-deriving its sibling group now, since a closing element may
    /// already have removed itself from that same list by the time this runs.
    /// </summary>
    private void RedirectFocusAwayFrom()
    {
        var closingElement = _focusedElement;
        if (closingElement is null)
        {
            return;
        }

        UnsubscribeFocusTracking();

        Element? nextSibling = null;
        if (_focusedElementSiblings is not null)
        {
            foreach (var candidate in _focusedElementSiblings)
            {
                if (candidate != closingElement && candidate.IsVisible && candidate.CanUserFocus)
                {
                    nextSibling = candidate;
                }
            }
        }

        _focusedElement = null;
        _focusedElementSiblings = null;
        SetFocus(nextSibling);
    }

    /// <summary>See RedirectFocusAwayFrom for why this deliberately excludes the Base tier.</summary>
    private IReadOnlyList<Element>? GetSiblingContainer(Element element) =>
        element.ParentElement?.ChildElements
        ?? (_layers.LayerOf(element) == UiLayer.DynamicHud ? _layers[UiLayer.DynamicHud] : null);

    /// <summary>
    /// Advances focus to the next (direction 1) or previous (direction -1) focusable Base/
    /// StaticHUD element (Element.CanUserFocus), wrapping past either end. Base and StaticHud only --
    /// fixed, distinct panels -- not DynamicHud, whose popups are dismissed by their own buttons
    /// rather than tabbed to, nor User, which holds nothing focusable.
    /// </summary>
    private void CycleFocus(int direction)
    {
        var focusableElements = new List<Element>();
        foreach (var element in _layers[UiLayer.Base])
        {
            if (element.IsVisible && element.CanUserFocus)
            {
                focusableElements.Add(element);
            }
        }

        foreach (var element in _layers[UiLayer.StaticHud])
        {
            if (element.IsVisible && element.CanUserFocus)
            {
                focusableElements.Add(element);
            }
        }

        if (focusableElements.Count == 0)
        {
            return;
        }

        var currentRoot = _focusedElement is not null
            ? GetRootAncestor(_focusedElement)
            : null;
        var currentIndex = currentRoot is not null
            ? focusableElements.IndexOf(currentRoot)
            : -1;

        // Nothing focused yet: forward starts at the first element, backward at the last --
        // matching standard Tab/Shift+Tab behavior from "nothing focused" -- rather than both
        // directions landing on the same element, which naive modulo wrapping from -1 would do.
        var unfocusedStartIndex = direction > 0
            ? 0
            : focusableElements.Count - 1;
        var nextIndex = currentIndex < 0
            ? unfocusedStartIndex
            : ((currentIndex + direction) % focusableElements.Count + focusableElements.Count) % focusableElements.Count;

        SetFocus(focusableElements[nextIndex]);
    }

    /// <summary>Forwards every key newly pressed this frame to whichever element holds focus. Tab is excluded since it's already claimed above for focus-cycling.</summary>
    /// <remarks>Skipped when the keyboard is unchanged since last frame -- nothing can be newly pressed then, and GetPressedKeys allocates an array every call.</remarks>
    private void RouteKeyPressesToFocusedElement(KeyboardState keyboardState)
    {
        if (_focusedElement is null || keyboardState == _previousKeyboardState)
        {
            return;
        }

        foreach (var key in keyboardState.GetPressedKeys())
        {
            if (key != Keys.Tab && _previousKeyboardState.IsKeyUp(key))
            {
                _focusedElement.HandleKeyPress(key);
            }
        }
    }

    /// <summary>
    /// Drains characters buffered by OnTextInput (see the constructor's TextInputEXT
    /// subscription) to whichever element holds focus. A separate buffer-then-drain step,
    /// unlike RouteKeyPressesToFocusedElement's direct poll of KeyboardState, because
    /// TextInputEXT.TextInput is an event, not a per-frame state snapshot -- characters can
    /// arrive between Update calls and need to be collected rather than read live.
    /// </summary>
    private void RouteTextInputToFocusedElement()
    {
        foreach (var character in _pendingTextInput)
        {
            _focusedElement?.HandleTextInput(character);
        }

        _pendingTextInput.Clear();
    }

    /// <summary>
    /// Computes the relative position and size a resize drag should produce this frame. Right/
    /// Bottom grow the size directly (dragStartSize plus delta) with no position change.
    /// Left/Top must derive the position shift from the *actual clamped* size, not the raw
    /// drag delta -- otherwise the pinned (opposite) edge drifts once the drag exceeds
    /// MinimumSize/MaximumSize, since the position shift and the size shrink have
    /// to match exactly to keep that edge visually fixed. All four edges can combine (a corner
    /// drag sets two of them at once).
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ComputeResize(Element element, ResizeEdges edges, Vector2 dragStartRelativePosition, Vector2 dragStartSize, Vector2 dragDelta)
    {
        var relativePosition = dragStartRelativePosition;
        var size = dragStartSize;

        if (edges.HasFlag(ResizeEdges.Right))
        {
            size.X = MathHelper.Clamp(dragStartSize.X + dragDelta.X, element.MinimumSize.X, element.MaximumSize.X);
        }
        if (edges.HasFlag(ResizeEdges.Bottom))
        {
            size.Y = MathHelper.Clamp(dragStartSize.Y + dragDelta.Y, element.MinimumSize.Y, element.MaximumSize.Y);
        }
        if (edges.HasFlag(ResizeEdges.Left))
        {
            var clampedWidth = MathHelper.Clamp(dragStartSize.X - dragDelta.X, element.MinimumSize.X, element.MaximumSize.X);
            relativePosition.X = dragStartRelativePosition.X + (dragStartSize.X - clampedWidth);
            size.X = clampedWidth;
        }
        if (edges.HasFlag(ResizeEdges.Top))
        {
            var clampedHeight = MathHelper.Clamp(dragStartSize.Y - dragDelta.Y, element.MinimumSize.Y, element.MaximumSize.Y);
            relativePosition.Y = dragStartRelativePosition.Y + (dragStartSize.Y - clampedHeight);
            size.Y = clampedHeight;
        }

        return (relativePosition, size);
    }

    /// <summary>
    /// The space an element's RelativePosition/Size are measured against: a root element's is the
    /// screen itself (RelativePosition doubles as its absolute screen position, see
    /// Window.BuildWindow), a child element's is its parent's own content area (RelativePosition
    /// is relative to ContentAbsolutePosition).
    /// </summary>
    private Vector2 GetPositionBounds(Element element) => element.ParentElement?.ContentSize ?? _screenSize;

    /// <summary>
    /// Pulls a drag-to-move's destination position back inside GetPositionBounds -- called with
    /// size unchanged, so an element dragged toward/past an edge simply stops there instead of
    /// continuing to follow the mouse off-screen (or out of its parent's content area). Forwards to
    /// RectangleClamp, shared with WindowCascadePlacement/PopupPositioning's own off-screen guards.
    /// </summary>
    private static Vector2 ClampMoveToBounds(Vector2 position, Vector2 size, Vector2 bounds) => RectangleClamp.ClampInside(position, size, bounds);

    /// <summary>
    /// Pulls a drag-to-resize's destination position+size back inside bounds. Unlike a move
    /// clamp (which only ever adjusts position), overflowing the left/top edge here must shrink
    /// the size by the overflow amount (not just clamp position) -- the element is being resized,
    /// not relocated, so running out of room at the edge being dragged should stop that edge
    /// growing further rather than sliding the whole element back on-screen. The right/bottom
    /// edges never move on their own (see ComputeResize), so overflow there is always a pure
    /// size reduction with no position change.
    ///
    /// minimumSize is re-applied as a floor after the overflow reductions above, not folded into
    /// them -- ComputeResize already clamps size to [MinimumSize, MaximumSize] against the drag
    /// delta alone, but the overflow correction runs afterward and, dragging a left/top edge far
    /// past the screen's edge, could otherwise shrink the size to zero or below. Clamping size up to
    /// minimumSize here can leave the right/bottom edge slightly past bounds in the same drag --
    /// an acceptable tradeoff, since a window that must be at least minimumSize can't always also
    /// fit entirely on screen while pinned at a corner.
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ClampResizeToBounds(Vector2 relativePosition, Vector2 size, Vector2 bounds, Vector2 minimumSize)
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

        size.X = MathHelper.Max(size.X, minimumSize.X);
        size.Y = MathHelper.Max(size.Y, minimumSize.Y);

        return (relativePosition, size);
    }

    /// <summary>
    /// Sets the OS cursor for whatever's under the mouse right now: the active drag's own
    /// cursor while one is in progress (regardless of where the mouse has since wandered --
    /// e.g. a resize drag dragged inward past the border still shows the resize cursor, not
    /// whatever the mouse happens to be over), otherwise what the hovered element (see
    /// RefreshHoveredInteraction) and the handlers' refusals say. Only calls MouseCursorEXT.SetCursor
    /// when the cursor actually changes, to avoid a native call every single frame regardless of
    /// whether anything changed.
    /// </summary>
    private void UpdateCursor(MouseState mouseState)
    {
        var position = new Point(mouseState.X, mouseState.Y);

        MouseCursor cursor;
        if (_activeInteraction.Kind == ElementDragInteractionKind.Resize)
        {
            cursor = GetResizeCursor(_activeInteraction.Edges);
            SetHoveredButton(null); // No hover feedback while a drag is actually in progress.
            SetHoveredScrollbar(null, ScrollbarPart.None);
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.Move)
        {
            cursor = MouseCursor.SizeAll;
            SetHoveredButton(null);
            SetHoveredScrollbar(null, ScrollbarPart.None);
        }
        else if (_activeInteraction is { Kind: ElementDragInteractionKind.PointerDrag, Element: { } pointerDragElement })
        {
            cursor = pointerDragElement.PointerDragCursor;
            SetHoveredButton(null);
            SetHoveredScrollbar(null, ScrollbarPart.None);
        }
        else if (_activeInteraction.Kind is ElementDragInteractionKind.ScrollThumb or ElementDragInteractionKind.ScrollTrack)
        {
            cursor = MouseCursor.Arrow;
            SetHoveredButton(null);
        }
        else
        {
            RefreshHoveredInteraction(position);

            if (IsContentDropRefusedAt(position) || IsInputRefusedAt(position))
            {
                cursor = MouseCursor.No;
                SetHoveredButton(null);
            }
            else
            {
                cursor = GetHoverCursor(_hoveredInteraction);
                SetHoveredButton(_hoveredInteraction.Button);
                SetHoveredScrollbar(_hoveredInteraction.ScrollbarPart != ScrollbarPart.None ? _hoveredInteraction.Element : null, _hoveredInteraction.ScrollbarPart);
            }
        }

        if (cursor != CurrentCursor)
        {
            MouseCursorEXT.SetCursor(cursor);
            CurrentCursor = cursor;
        }
    }

    /// <summary>
    /// Hit-tests position into _hoveredInteraction, only when the mouse has moved, the layers have changed, or an input
    /// since the last hit-test may have changed what's under the cursor -- a full recursive walk over every element
    /// across every tier and their descendants, which otherwise ran every frame whether or not anything moved. Not
    /// refreshed during a window move, resize or scrollbar drag, which hover nothing; the first frame after one re-tests
    /// wherever the mouse ended up.
    /// </summary>
    /// <remarks>
    /// Re-tested after every press, release, wheel scroll and keyboard change, and whenever UiLayerStack.Version
    /// changes, so a window opened by a click, a hotkey or the game, or content scrolled under the cursor, is seen the
    /// same frame. A child element replaced inside a window with no input at all (a list rebuilt by the game) is seen
    /// once the mouse next moves.
    /// </remarks>
    private void RefreshHoveredInteraction(Point position)
    {
        if (_hoveredInteractionPosition == position && _hoveredInteractionLayersVersion == _layers.Version)
        {
            return;
        }

        _hoveredInteraction = TryHitTestInteraction(position);
        _hoveredInteractionPosition = position;
        _hoveredInteractionLayersVersion = _layers.Version;
    }

    /// <summary>Makes the next RefreshHoveredInteraction hit-test again whatever the position -- for an input that may have changed what's under the cursor.</summary>
    private void InvalidateHoveredInteraction() => _hoveredInteractionPosition = null;

    /// <summary>The element under position as of now: _hoveredInteraction, refreshed if it's out of date.</summary>
    private ElementInteraction HoveredInteractionAt(Point position)
    {
        RefreshHoveredInteraction(position);
        return _hoveredInteraction;
    }

    /// <summary>
    /// True while the hovered element is something an input handler says refuses to be used right now (see
    /// IUiInputHandler.IsInputRefusedAt). Asked every frame, since a refusal can change under a still mouse.
    /// </summary>
    private bool IsInputRefusedAt(Point position)
    {
        foreach (var handler in _inputHandlers)
        {
            if (handler.IsInputRefusedAt(_hoveredInteraction.Element, position))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while a content drag is held and a drop handler says its payload can't be dropped on the hovered element (see IContentDropHandler.IsDropRefusedAt). Asked every frame, so the cursor reads correctly for every frame of a still hover, not just the one it first arrived on.</summary>
    private bool IsContentDropRefusedAt(Point position)
    {
        if (_contentDrag is not { } contentDrag)
        {
            return false;
        }

        foreach (var handler in _contentDropHandlers)
        {
            if (handler.IsDropRefusedAt(contentDrag.Payload, _hoveredInteraction.Element, position))
            {
                return true;
            }
        }

        return false;
    }

    private static MouseCursor GetHoverCursor(ElementInteraction interaction) => interaction.Kind switch
    {
        ElementDragInteractionKind.Resize => GetResizeCursor(interaction.Edges),
        ElementDragInteractionKind.Move => MouseCursor.SizeAll,
        ElementDragInteractionKind.PointerDrag when interaction.Element is { } element => element.PointerDragCursor,
        ElementDragInteractionKind.None when interaction.Element is TextBox => MouseCursor.IBeam,
        _ => MouseCursor.Arrow,
    };

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
