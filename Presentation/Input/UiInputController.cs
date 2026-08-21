using Engine.ECS.Components;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;
using Presentation.UI.Content;

namespace Presentation.Input;

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

    /// <summary>How long Escape must be held (not just tapped) before it closes every closeable DynamicHUD window at once instead of just the topmost -- see HandleEscape. Comfortably longer than DoubleTapWindowFrames-style thresholds elsewhere (0.3s), so an ordinary tap can never accidentally read as a hold.</summary>
    private static readonly int EscapeHoldCloseAllFrames = GameTiming.FramesForSeconds(0.5f);

    /// <summary>Consecutive frames Escape has been held down, 0 while it's up -- HandleEscape's own edge/hold distinction (1 == a fresh press).</summary>
    private int _escapeHeldFrames;

    /// <summary>Guards CloseAllClosableWindows to firing once per hold, not every frame past EscapeHoldCloseAllFrames -- reset the moment Escape is released.</summary>
    private bool _escapeHoldCloseAllFired;

    private readonly UiLayerStack _layers;
    private readonly Vector2 _screenSize;

    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;

    private ElementInteraction _activeInteraction = ElementInteraction.NotHit;
    private Vector2 _dragStartMousePosition;
    private Vector2 _dragStartRelativePosition;
    private Vector2 _dragStartSize;

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
    /// ElementDragInteractionKind or the item/hotbar content-drag mechanism: neither of those
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
    /// Left-button content-drag state: an inventory item stack cell (InventoryItemStackCell) or
    /// an already-bound hotbar slot (HotbarContent, item or action) picked up on press, carried
    /// as a plain StackInstanceId or ActionId payload (mutually exclusive -- see
    /// _contentDragActionId), and resolved against whatever's under the cursor on release -- see
    /// HandleMousePress/ResolveContentDrag. Deliberately narrow (item-cell/bound-slot &lt;-&gt;
    /// hotbar-slot only), not a generic Element-level drag-and-drop framework the way
    /// Move/Resize is: a future Equipment menu drop target would add its own resolution branch
    /// here rather than this becoming a virtual-hook mechanism every Element opts into.
    /// Independent of _activeInteraction.Kind (which stays None for a plain content click on
    /// either source) -- this is tracked entirely by its own fields instead.
    /// </summary>
    private Guid? _contentDragItemStackInstanceId;

    /// <summary>
    /// Set specifically while dragging a Merged Stack cell -- one with no single StackInstanceId
    /// (see InventoryItemStackCell.CanBindToHotbar's own doc comment for the Base/Diverging/Merged
    /// vocabulary) -- to that cell's own ItemDefinitionId, the representative item every member
    /// shares. Its non-null-ness is itself the "dragging a Merged Stack" signal (never set at the
    /// same time as _contentDragItemStackInstanceId -- a given drag is either a resolved single
    /// stack or an unresolved merge, never both); its value exists purely so DragGhostContent has
    /// something to resolve an icon from (see ContentDragMergedItemDefinitionId) -- there's no
    /// StackInstanceId to look up an icon through for a merge. Still a real, tracked drag
    /// otherwise -- ghost, hotbar highlight, everything _contentDragItemStackInstanceId would
    /// otherwise drive -- so a future manual-sort gesture has something to build on; it's refused
    /// only at the actual moment of dropping onto a hotbar slot (see ResolveContentDrag), with a
    /// MouseCursor.No shown the whole time it hovers one (see IsContentDragBlockedAt).
    /// </summary>
    private Guid? _contentDragMergedItemDefinitionId;

    /// <summary>Same role as _contentDragItemStackInstanceId, for a drag that started on an already-bound action slot -- never both set at once (a slot binds to at most one of {action, item}, see IHotkeySlotBinding). There's no action-cell drag source today (no spellbook/action-list UI exists), so this is only ever populated via a bound hotbar slot, unlike the item payload which can also come from InventoryItemStackCell.</summary>
    private Guid? _contentDragActionId;

    /// <summary>The dragged source's own on-screen size at the moment the drag started -- InventoryItemStackCell.CurrentSize for an inventory cell, HotbarContent.SlotSize for a hotbar slot (the slot, not the whole hotbar window). DragGhostContent draws the ghost at this size rather than one fixed size for every drag, so it doesn't visibly jump in scale relative to wherever it was actually picked up from.</summary>
    private Vector2 _contentDragSourceSize;

    /// <summary>Set alongside _contentDragItemStackInstanceId/_contentDragActionId only when the drag started on an already-bound hotbar slot -- that slot's binding is removed on release regardless of where the drag ends (see ResolveContentDrag), so dragging an item/action off the hotbar entirely un-assigns it.</summary>
    private HotbarContent? _contentDragOriginHotbar;
    private HotkeySlot? _contentDragOriginSlot;

    /// <summary>The entity whose inventory grid the drag started from -- set only when the drag started on an InventoryItemStackCell (see InventoryItemStackCell.EntityId), never for a hotbar-origin drag. Read by ResolveContentDrag to detect an inventory-to-inventory transfer: dropped onto a different entity's own grid, not this one.</summary>
    private int? _contentDragOriginEntityId;

    /// <summary>Mouse position when the current content-drag started -- ResolveContentDrag only actually resolves a drop if the release is at least ContentDragTapThresholdPixels away, so a plain click on a cell/slot (no real drag) doesn't spuriously unbind-then-rebind it.</summary>
    private Vector2 _contentDragStartMousePosition;

    /// <summary>Same reasoning/value as RightClickTapThresholdPixels -- small enough to absorb ordinary click jitter, comfortably smaller than an intentional drag.</summary>
    private const float ContentDragTapThresholdPixels = 4f;

    /// <summary>Frames since the current content-drag payload was captured (see TryStartContentDrag) -- reset there, incremented once per Update while a payload is held. Gates ContentDragGhostVisible below.</summary>
    private int _contentDragHeldFrames;

    private static readonly int ContentDragGhostDelayFrames = GameTiming.FramesForSeconds(0.15f);

    /// <summary>Owns the Armed Hotkey Summary window's arm/preview/hover state machine -- see its own doc comment. Null in test setups that don't build one (e.g. UiInputControllerTests' own harness), in which case hotbar-slot press/release/hover handling is simply skipped.</summary>
    private readonly HotbarController? _hotbarController;

    /// <summary>Needed only for the inventory-to-inventory drag-transfer branch of ResolveContentDrag (InventoryActions.TryTransferStack/TryTransferAllStacksOfItem) -- null in test setups that don't wire one, in which case that branch is simply skipped and a content-drag can still resolve against a hotbar slot as before.</summary>
    private readonly ComponentManager? _componentManager;

    /// <summary>See _componentManager -- passed straight through to InventoryActions' capacity check for a non-player transfer destination.</summary>
    private readonly IPlayerQuery? _playerQuery;

    /// <summary>Mouse position when the current hotbar-slot press started -- ResolveHotbarSlotClick only treats the release as a tap if it's within ContentDragTapThresholdPixels of this, the same tap-vs-drag distinction ResolveContentDrag already makes for content-drags.</summary>
    private Vector2 _hotbarPressMousePosition;

    private Element? _focusedElement;

    /// <summary>
    /// The container (a parent's ChildElements, or the DynamicHUD tier) the currently focused
    /// element belonged to at the moment it gained focus -- see GetSiblingContainer and
    /// RedirectFocusAwayFrom. Snapshotted in SetFocus rather than recomputed at close/minimize
    /// time because a closing element may already have removed itself from that same list by
    /// then (e.g. NotificationCenter.OnActiveNotificationClosed), depending on event
    /// subscription order.
    /// </summary>
    private IReadOnlyList<Element>? _focusedElementSiblings;

    /// <summary>The fallback focus target whenever a close/minimize redirect (see RedirectFocusAwayFrom) finds no sibling to move to -- e.g. dismissing the last active notification, or closing the quest composer popup. Set once via SetDefaultFocusElement, the same composition-root role FocusElement already plays for initial focus.</summary>
    private Element? _defaultFocusElement;

    /// <summary>
    /// The focused element itself plus every ParentElement above it, up to its root -- e.g. a
    /// focused TextBox's chain is [textBox, popup]. Closing a window only ever fires Closed on
    /// that exact element, never on its still-open descendants (RemoveChildWindow doesn't raise
    /// anything on the child being removed) -- so closing the quest-composer popup while its
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
    /// environment with no actual SDL window backing it (confirmed: asserting on it directly
    /// still reads false immediately after a real StartTextInput() call). Tests substitute a
    /// call-recording fake and assert on that instead.
    /// </summary>
    internal Action StartTextInput = TextInputEXT.StartTextInput;

    /// <summary>See StartTextInput.</summary>
    internal Action StopTextInput = TextInputEXT.StopTextInput;

    /// <summary>
    /// userElements is typically still empty at construction time -- ShellBootstrapper.Build
    /// constructs UiInputController before it can build DragGhostContent (which needs a real
    /// UiInputController reference to read the drag state from), then appends the ghost's host
    /// window to this same list afterward. Passing the list itself (not a snapshot/copy) is what
    /// makes that work -- this class only ever reads through the reference, never replaces it.
    /// </summary>
    public UiInputController(UiLayerStack layers, Vector2 screenSize, HotbarController? hotbarController = null, ComponentManager? componentManager = null, IPlayerQuery? playerQuery = null)
    {
        _layers = layers;
        _screenSize = screenSize;
        _hotbarController = hotbarController;
        _componentManager = componentManager;
        _playerQuery = playerQuery;

        // Subscribing is safe to do unconditionally and permanently -- SDL simply never raises
        // SDL_TEXTINPUT while text input is stopped (see SetFocus's Start/StopTextInput calls
        // below), so this just never fires until a TextBox is actually focused.
        TextInputEXT.TextInput += OnTextInput;
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
    /// anywhere gets this for free rather than each owner self-polling Mouse.GetState() the way
    /// AbilityScoreWindow/AbilityScoreModifierRow do for their own unrelated hover popups.
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

    /// <summary>The drag/resize interaction currently in progress (or ElementInteraction.NotHit if none). Move is wired to SetRelativePosition and Resize to SetBounds, both each held frame -- see ComputeResize for the resize math.</summary>
    internal ElementInteraction ActiveInteraction => _activeInteraction;

    /// <summary>The element currently holding keyboard focus, if any -- see SetFocus/RouteKeyPressesToFocusedElement/CycleFocus.</summary>
    internal Element? FocusedElement => _focusedElement;

    /// <summary>
    /// Whether a TextBox currently holds keyboard focus -- public (unlike FocusedElement) so
    /// consumers in other assemblies (e.g. ShellBootstrapper wiring MapWindow.IsTextInputFocused,
    /// so Space doesn't pause the game while typing) can check without needing the actual
    /// focused-element reference.
    /// </summary>
    public bool IsTextBoxFocused => _focusedElement is TextBox;

    /// <summary>Focuses an element from outside -- GameLoop calls this once at startup to default-focus the map window, since an element's own hotkeys (see RouteHotkeysToFocusedElement) only fire while it holds focus.</summary>
    public void FocusElement(Element element) => SetFocus(element);

    /// <summary>See _defaultFocusElement.</summary>
    public void SetDefaultFocusElement(Element element) => _defaultFocusElement = element;

    /// <summary>Element.RelativePosition captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. Move's per-frame SetRelativePosition is this plus DragDelta; ComputeResize uses it as the Left/Top-edge resize baseline.</summary>
    internal Vector2 DragStartRelativePosition => _dragStartRelativePosition;

    /// <summary>Element.CurrentSize captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. ComputeResize's resize baseline.</summary>
    internal Vector2 DragStartSize => _dragStartSize;

    /// <summary>Mouse movement since the drag started, recomputed every held frame -- zero on the press frame itself and once released.</summary>
    internal Vector2 DragDelta { get; private set; }

    /// <summary>The last cursor UpdateCursor set (or the initial Arrow default, if it's never had reason to change) -- lets tests assert on cursor selection without depending on real OS cursor state.</summary>
    internal MouseCursor CurrentCursor { get; private set; } = MouseCursor.Arrow;

    /// <summary>The item currently being content-dragged, if any -- see _contentDragItemStackInstanceId's own doc comment. Public (unlike most of this class's internals) so ShellBootstrapper -- a different assembly -- can wire it into DragGhostContent.GetState, the same reasoning IsTextBoxFocused's own doc comment gives.</summary>
    public Guid? ContentDragItemStackInstanceId => _contentDragItemStackInstanceId;

    /// <summary>The Merged Stack cell's own ItemDefinitionId currently being content-dragged, if any -- see _contentDragMergedItemDefinitionId's own doc comment. Public for the same reason as ContentDragItemStackInstanceId above.</summary>
    public Guid? ContentDragMergedItemDefinitionId => _contentDragMergedItemDefinitionId;

    /// <summary>The action currently being content-dragged, if any -- see _contentDragActionId's own doc comment. Public for the same reason as ContentDragItemStackInstanceId above.</summary>
    public Guid? ContentDragActionId => _contentDragActionId;

    /// <summary>The entity ContentDragItemStackInstanceId/ContentDragMergedItemDefinitionId's stack actually belongs to, if the drag started on an InventoryItemStackCell -- see _contentDragOriginEntityId's own doc comment. Public for the same reason as ContentDragItemStackInstanceId above -- DragGhostContent needs this to resolve a corpse-originated drag's icon, not just the player's own inventory.</summary>
    public int? ContentDragOriginEntityId => _contentDragOriginEntityId;

    /// <summary>Whether DragGhostContent should actually draw the ghost right now -- true once a content-drag payload has been held for ContentDragGhostDelayFrames. See that constant's own doc comment for why this delay exists. Public for the same reason as ContentDragItemStackInstanceId above.</summary>
    public bool ContentDragGhostVisible =>
        (_contentDragItemStackInstanceId is not null || _contentDragMergedItemDefinitionId is not null || _contentDragActionId is not null) && _contentDragHeldFrames >= ContentDragGhostDelayFrames;

    /// <summary>See _contentDragSourceSize's own doc comment. Public for the same reason as ContentDragItemStackInstanceId above.</summary>
    public Vector2 ContentDragSourceSize => _contentDragSourceSize;

    /// <summary>Current mouse screen position, refreshed at the top of every Update call -- paired with ContentDragItemStackInstanceId for the same ghost-sprite use, and with CursorTextContent.GetCursorPosition. Public for the same reason as ContentDragItemStackInstanceId above.</summary>
    public Point CurrentMousePosition { get; private set; }

    public void Update(GameTime gameTime) => Update(Keyboard.GetState(), Mouse.GetState());

    /// <summary>
    /// Takes explicit states rather than reading Keyboard.GetState()/Mouse.GetState() itself,
    /// so tests can drive synthetic press/release/move sequences -- the public
    /// Update(GameTime) overload above is the only real caller otherwise.
    /// </summary>
    internal void Update(KeyboardState keyboardState, MouseState mouseState)
    {
        CurrentMousePosition = new Point(mouseState.X, mouseState.Y);

        RouteHotkeysToFocusedElement(keyboardState);
        HandleFocusCycling(keyboardState);
        HandleEscape(keyboardState);
        RouteKeyPressesToFocusedElement(keyboardState);
        RouteTextInputToFocusedElement();

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
            HandleRightDragEnd();
        }
        else if (mouseState.RightButton == ButtonState.Pressed && _rightDragElement is not null)
        {
            HandleRightDrag(mouseState);
        }

        UpdateMouseWheelScroll(mouseState);
        UpdateCursor(mouseState);
        HandleHotbarHover(mouseState);

        if (_contentDragItemStackInstanceId is not null || _contentDragMergedItemDefinitionId is not null || _contentDragActionId is not null)
        {
            _contentDragHeldFrames++;
        }

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
    }

    /// <summary>
    /// Routes the whole keyboard state to whichever element is focused, once per frame (see
    /// Window.HandleHotkeys) -- e.g. MapWindow's WASD/zoom/PageUp/PageDown/Space, or a TextBox's
    /// arrow-key/Backspace/copy-paste handling (see TextBox.OnHotkeysAction's own doc comment --
    /// those route through HandleHotkeys, not the discrete key-press path, for held-key repeat).
    /// UiInputController itself knows nothing about what any element's hotkeys are; it only
    /// knows which element is focused. While menu mode is active, this is scoped by
    /// IsReachableDuringMenuMode the same way TryHitTestInteraction already scopes mouse
    /// hit-testing -- routed if the focused element is actually part of what's currently
    /// interactive (e.g. a TextBox inside an open Inventory window), skipped otherwise (e.g.
    /// MapWindow itself, still focused from before a menu window opened, must not have its own
    /// WASD/arm-hotkeys or Space-pause reach the blocked, dimmed world underneath).
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

        /// <summary>Element.HandleEscape() (see Element.OnEscapeAction) is called on every element in this layer, unconditionally -- for layers whose own elements need to react to Escape themselves regardless of focus (e.g. MapWindow canceling an armed ability/item).</summary>
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
        _ => throw new NotImplementedException($"No Escape behavior defined for UiLayer.{layer}."),
    };

    /// <summary>
    /// Escape must stay unconditional too, the same reasoning as Tab above: broadcast to every
    /// BroadcastToElements-layer element (not just whichever holds focus) rather than routed only
    /// to the focused one, since an armed action's own window (MapWindow) shouldn't have to hold
    /// keyboard focus for Escape to cancel it -- e.g. a StaticHUD panel could be focused while
    /// the map still has an action armed. No-op by default (Window.OnEscapeAction); MapWindow
    /// is the only override today. This is scoped to action-cancel only -- the separate "Escape
    /// opens the options menu" TODO.md item isn't implemented here.
    ///
    /// A fresh press (the first held frame) also closes the frontmost closeable window across
    /// every CloseableWindows-layer -- notification popups, the Inventory/Ability Score windows,
    /// the quest composer, anything else those layers ever grow -- one at a time, same as
    /// clicking its own close button would (see CloseTopmostClosableWindow). Continuing to hold
    /// Escape past EscapeHoldCloseAllFrames instead sweeps every closeable window closed at once
    /// (see CloseAllClosableWindows), so a player buried under several popups can clear them all
    /// without repeated presses.
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
            // While menu mode is active, Escape must not reach through it to Base/StaticHud
            // elements behind it (e.g. MapWindow canceling an armed ability) -- that's exactly
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
            CloseAllClosableWindows();
        }
    }

    /// <summary>
    /// Closes just the frontmost (highest CloseableWindows layer, then last-raised/drawn-on-top
    /// within it) closeable window -- a Window with CanUserClose true. Deliberately excludes
    /// non-Window elements (the Notification/Inventory Folder icons, plain Element subclasses)
    /// and Windows with CanUserClose false (the Armed Hotkey Summary) -- neither is something a
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

    /// <summary>
    /// Same eligibility as CloseTopmostClosableWindow, but closes every match across every
    /// CloseableWindows layer, topmost first. Each layer's list is snapshotted into an array
    /// before iterating: Window.Close() removes itself from its layer via its own Closed handler
    /// (see NotificationCenter.OnActiveNotificationClosed / InventoryFolderController.WindowSlot.
    /// HandleClosed), which would otherwise corrupt an in-progress enumeration of that same live
    /// list -- the same snapshot-first reasoning ElementPoolService.CloseAllChildren already uses.
    /// </summary>
    private void CloseAllClosableWindows()
    {
        // Same menu-mode exclusivity reasoning as CloseTopmostClosableWindow above -- a held
        // Escape while menu mode is active still only dismisses the frontmost menu window, not
        // every closeable window.
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

            var snapshot = _layers[layer].ToArray();
            for (var index = snapshot.Length - 1; index >= 0; index--)
            {
                if (snapshot[index] is Window { CanUserClose: true } window)
                {
                    window.Close();
                }
            }
        }
    }

    private void HandleMousePress(MouseState mouseState)
    {
        var clickPosition = new Point(mouseState.X, mouseState.Y);

        _activeInteraction = TryHitTestInteraction(clickPosition);
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
                // unrelated child happens to be focusable -- confirmed bug: clicking a window's
                // Close button was landing focus on its tab search box (or, with no such
                // descendant, just highlighting the title bar as focused) purely because the
                // window itself is focusable, with nothing to do with what was actually clicked.
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
            }
        }
        else
        {
            SetFocus(null);
        }

        TryStartContentDrag(clickPosition);
        CaptureHotbarPressSlot(clickPosition);
    }

    /// <summary>
    /// Records which hotbar slot (if any) the press landed on, for ResolveHotbarSlotClick to
    /// compare against on release -- a separate method from TryStartContentDrag, not a widening of
    /// it, since that method's own gate (TryGetBoundItemId, item-only) is narrower by design for
    /// drag-payload capture; reusing/widening it here would risk accidentally enabling drag-rebind
    /// for ability slots.
    /// </summary>
    private void CaptureHotbarPressSlot(Point clickPosition)
    {
        if (_hotbarController is null)
        {
            return;
        }

        // Recorded on every press regardless of what was hit -- ResolveHotbarSlotClick's own
        // re-hit-test of the release position is what actually gates whether a tap fires, so this
        // just needs to always reflect the most recent press position rather than going stale.
        _hotbarPressMousePosition = new Vector2(clickPosition.X, clickPosition.Y);

        if (_activeInteraction.Element is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(clickPosition, out var pressedSlot))
        {
            _hotbarController.OnSlotPressed(pressedSlot);
        }
        else
        {
            _hotbarController.OnPressOutsideHotbar();
        }
    }

    /// <summary>
    /// Captures a content-drag payload if the press landed on a drag source -- an
    /// InventoryItemStackCell (its own ItemDefinitionId and CurrentSize), or a HotbarContent slot
    /// that already has an item or action bound (that item's/action's id, HotbarContent.SlotSize,
    /// and which slot/HotbarContent to unbind from on release -- see _contentDragOriginHotbar's
    /// own doc comment). Independent of _activeInteraction.Kind: a plain content click on either
    /// source resolves to Kind None, so this runs unconditionally on every press rather than
    /// being folded into the Kind-gated branch above.
    /// </summary>
    private void TryStartContentDrag(Point clickPosition)
    {
        _contentDragItemStackInstanceId = null;
        _contentDragMergedItemDefinitionId = null;
        _contentDragActionId = null;
        _contentDragOriginHotbar = null;
        _contentDragOriginSlot = null;
        _contentDragOriginEntityId = null;
        _contentDragHeldFrames = 0;

        // Every InventoryItemStackCell is a real drag source, Merged Stack included (see
        // CanBindToHotbar's own doc comment for the Base/Diverging/Merged vocabulary) -- a merged
        // cell just carries no StackInstanceId to bind with, tracked instead via
        // _contentDragMergedItemDefinitionId (its own ItemDefinitionId, for DragGhostContent's
        // icon) so ResolveContentDrag/IsContentDragBlockedAt can refuse the bind specifically,
        // without refusing the drag itself.
        if (_activeInteraction.Element is InventoryItemStackCell cell)
        {
            if (cell.StackInstanceId is { } cellStackInstanceId)
            {
                _contentDragItemStackInstanceId = cellStackInstanceId;
            }
            else
            {
                _contentDragMergedItemDefinitionId = cell.ItemDefinitionId;
            }

            _contentDragOriginEntityId = cell.EntityId;
            _contentDragSourceSize = cell.CurrentSize;
            _contentDragStartMousePosition = new Vector2(clickPosition.X, clickPosition.Y);
        }
        else if (_activeInteraction.Element is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(clickPosition, out var pressedSlot))
        {
            if (hotbarContent.TryGetBoundItemStackInstanceId(pressedSlot, out var boundStackInstanceId))
            {
                _contentDragItemStackInstanceId = boundStackInstanceId;
                _contentDragSourceSize = HotbarContent.SlotSize;
                _contentDragOriginHotbar = hotbarContent;
                _contentDragOriginSlot = pressedSlot;
                _contentDragStartMousePosition = new Vector2(clickPosition.X, clickPosition.Y);
            }
            else if (hotbarContent.TryGetBoundActionId(pressedSlot, out var boundActionId))
            {
                _contentDragActionId = boundActionId;
                _contentDragSourceSize = HotbarContent.SlotSize;
                _contentDragOriginHotbar = hotbarContent;
                _contentDragOriginSlot = pressedSlot;
                _contentDragStartMousePosition = new Vector2(clickPosition.X, clickPosition.Y);
            }
        }

        if ((_contentDragItemStackInstanceId is not null || _contentDragMergedItemDefinitionId is not null || _contentDragActionId is not null) &&
            !IsDragFromNonPlayerInventory)
        {
            SetHotbarDragHighlight(true);
        }
    }

    /// <summary>Turns HotbarContent's drop-target glow (see its own IsAcceptingDrag doc comment) on/off -- the hotbar itself may or may not exist in every scene (e.g. a test harness with no StaticHUD at all), so this is a no-op if none of staticHudElements hosts one.</summary>
    private void SetHotbarDragHighlight(bool isAccepting)
    {
        foreach (var element in _layers[UiLayer.StaticHud])
        {
            if (element is Window { Content: HotbarContent hotbarContent })
            {
                hotbarContent.IsAcceptingDrag = isAccepting;
                return;
            }
        }
    }

    /// <summary>
    /// Feeds HotbarController.UpdateHover every frame with whichever bound hotbar slot (if any)
    /// the cursor is currently over -- unbound slots never count (hovering one while something
    /// else is armed/previewed elsewhere would otherwise blank the already-visible summary, since
    /// HotbarController resolves a null candidate as "nothing hovered" rather than "hovering
    /// nothing in particular"). Suppressed during an active drag -- a summary popping up mid-drag
    /// (rebinding a slot, moving a window) would just be visual noise over something the player is
    /// already doing.
    /// </summary>
    private void HandleHotbarHover(MouseState mouseState)
    {
        if (_hotbarController is null)
        {
            return;
        }

        HotkeySlot? candidateSlot = null;

        if (_activeInteraction.Kind == ElementDragInteractionKind.None)
        {
            var mousePosition = new Point(mouseState.X, mouseState.Y);
            foreach (var element in _layers[UiLayer.StaticHud])
            {
                if (element is Window { Content: HotbarContent hotbarContent } &&
                    hotbarContent.TryGetSlotAt(mousePosition, out var slot) &&
                    hotbarContent.TryGetSlotSummary(slot, out _, out _))
                {
                    candidateSlot = slot;
                    break;
                }
            }
        }

        _hotbarController.UpdateHover(candidateSlot);
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
        // A completed text-selection drag (see HandleTextSelectionDrag) skips the ordinary
        // content-click -- OnContentClickAction's plain-click branch would otherwise collapse the
        // selection the drag just made right back down to a bare caret at the release position.
        if (!_textSelectionDragExceededTapThreshold)
        {
            _activeInteraction.Element?.HandleClick(new Point(mouseState.X, mouseState.Y));
        }

        ResolveContentDrag(new Point(mouseState.X, mouseState.Y));
        ResolveHotbarSlotClick(new Point(mouseState.X, mouseState.Y));

        PressedButton?.SetPressed(false);
        PressedButton = null;
        _activeInteraction = ElementInteraction.NotHit;
        DragDelta = Vector2.Zero;
        _textSelectionDragBox = null;
        _textSelectionDragExceededTapThreshold = false;
    }

    /// <summary>
    /// Resolves an in-progress content-drag (see _contentDragItemStackInstanceId/_contentDragActionId's
    /// own doc comments) against the release position -- a no-op if the mouse never actually
    /// moved past ContentDragTapThresholdPixels (a plain click on a cell/slot must not
    /// spuriously unbind-then-rebind it). This method's own job is gesture recognition and
    /// resolving the drop target -- what actually happens to the origin/destination slots (the
    /// unbind-then-bind sequencing rule) is HotbarContent.ResolveDroppedBinding's job, since it
    /// belongs next to that class's own binding rules, not here (see its own doc comment).
    /// receivingHotbar prefers the origin (set if the drag started on an already-bound hotbar
    /// slot) and falls back to the drop target -- both ultimately mean "the hotbar this drag
    /// touches," and today there's only ever one HotbarContent instance for either to resolve to.
    /// Always clears the drag-highlight/state at the end, drop accepted or not, so a cancelled/
    /// missed drag never leaves the hotbar glowing or a stale payload behind for the next press
    /// to accidentally inherit.
    /// </summary>
    private void ResolveContentDrag(Point releasePosition)
    {
        if (_contentDragItemStackInstanceId is null && _contentDragActionId is null && _contentDragMergedItemDefinitionId is null)
        {
            return;
        }

        try
        {
            var releaseVector = new Vector2(releasePosition.X, releasePosition.Y);
            if (Vector2.Distance(_contentDragStartMousePosition, releaseVector) < ContentDragTapThresholdPixels)
            {
                return;
            }

            var dropInteraction = TryHitTestInteraction(releasePosition);

            // Inventory-to-inventory transfer: the drop landed inside another entity's own grid
            // window. Checked before the Merged-Stack-never-binds early return just below, since
            // (unlike hotbar binding) a Merged Stack CAN move as a whole batch between grids --
            // see InventoryActions.TryTransferAllStacksOfItem. The same-entity guard already lives
            // in InventoryActions itself (defense in depth) -- dropping back onto the origin
            // entity's own grid is a safe no-op either way, so it isn't special-cased here too.
            if (_componentManager is { } componentManager && _contentDragOriginEntityId is { } originEntityId &&
                FindHostingGrid(dropInteraction.Element) is { } dropGrid)
            {
                if (_contentDragItemStackInstanceId is { } stackInstanceId)
                {
                    InventoryActions.TryTransferStack(componentManager, originEntityId, dropGrid.EntityId, stackInstanceId, _playerQuery);
                }
                else if (_contentDragMergedItemDefinitionId is { } itemDefinitionId)
                {
                    InventoryActions.TryTransferAllStacksOfItem(componentManager, originEntityId, dropGrid.EntityId, itemDefinitionId, _playerQuery);
                }

                return;
            }

            // A Merged Stack's drag never binds to a hotbar slot -- there is no single stack a
            // drop there could mean (see InventoryItemStackCell.CanBindToHotbar's own doc
            // comment). IsContentDragBlockedAt already showed MouseCursor.No the whole time it
            // hovered a hotbar slot; the drag just ends here with no binding change, cleanup
            // happens in finally either way.
            if (_contentDragMergedItemDefinitionId is not null)
            {
                return;
            }

            var isActionDrag = _contentDragActionId is not null;
            var payloadId = isActionDrag ? _contentDragActionId!.Value : _contentDragItemStackInstanceId!.Value;

            var receivingHotbar = _contentDragOriginHotbar;
            HotkeySlot? destinationSlot = null;

            // An item dragged from another entity's own inventory (a corpse, say) can never bind
            // to the player's hotbar -- see IsDragFromNonPlayerInventory's own doc comment for why
            // (the bound stack wouldn't even belong to the player). Hotbar-origin drags (moving/
            // unbinding an already-bound slot) are unaffected -- ContentDragOriginEntityId is only
            // ever set for an InventoryItemStackCell-origin drag, never a hotbar one.
            if (!IsDragFromNonPlayerInventory && dropInteraction.Element is Window { Content: HotbarContent dropHotbar } &&
                dropHotbar.TryGetSlotAt(releasePosition, out var dropSlot))
            {
                receivingHotbar ??= dropHotbar;
                destinationSlot = dropSlot;
            }

            receivingHotbar?.ResolveDroppedBinding(_contentDragOriginSlot, destinationSlot, isActionDrag, payloadId);
        }
        finally
        {
            _contentDragItemStackInstanceId = null;
            _contentDragMergedItemDefinitionId = null;
            _contentDragActionId = null;
            _contentDragOriginHotbar = null;
            _contentDragOriginSlot = null;
            _contentDragOriginEntityId = null;
            _contentDragHeldFrames = 0;
            SetHotbarDragHighlight(false);
        }
    }

    /// <summary>
    /// Walks up from element through ParentElement looking for the nearest ancestor Window hosting
    /// an InventoryGridContent -- handles the drop landing on a specific InventoryItemStackCell or
    /// on empty grid space alike, matching "drop location is anywhere on the receiving entity's
    /// item grid." Checked via Window.Tag, not Window.Content -- InventoryGridContent isn't always
    /// assigned as its host window's Content (see InventoryGridContent.Initialize's own doc
    /// comment: InventoryTabContent's own hosting pattern drives it manually and never calls
    /// SetContent at all, unlike CorpseInventoryWindow's), so Tag is the one thing both hosting
    /// patterns reliably set (confirmed via a live-testing repro against the real, TabbedContent-
    /// nested InventoryManagementWindow structure -- Content-based matching silently found nothing
    /// there, even though it worked against a simpler hand-built test harness).
    /// </summary>
    private static InventoryGridContent? FindHostingGrid(Element? element)
    {
        for (var candidate = element; candidate is not null; candidate = candidate.ParentElement)
        {
            if (candidate is Window { Tag: InventoryGridContent grid })
            {
                return grid;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a hotbar-slot press/release pair into a tap on HotbarController, if the release
    /// is close enough to the press to count as a tap (not a drag -- same
    /// ContentDragTapThresholdPixels distinction ResolveContentDrag already makes) and lands on a
    /// hotbar slot at all. HotbarController.OnSlotTapped itself verifies the release slot matches
    /// whichever slot was actually pressed (see CaptureHotbarPressSlot), so a press-then-drag that
    /// happens to end back within the tap threshold, but over a different slot, doesn't
    /// spuriously count as a tap there.
    /// </summary>
    private void ResolveHotbarSlotClick(Point releasePosition)
    {
        if (_hotbarController is null)
        {
            return;
        }

        var releaseVector = new Vector2(releasePosition.X, releasePosition.Y);
        if (Vector2.Distance(_hotbarPressMousePosition, releaseVector) >= ContentDragTapThresholdPixels)
        {
            return;
        }

        var dropInteraction = TryHitTestInteraction(releasePosition);
        if (dropInteraction.Element is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(releasePosition, out var releasedSlot))
        {
            _hotbarController.OnSlotTapped(releasedSlot);
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
    }

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
        _rightDragElement = TryHitTestInteraction(position).Element;
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

    /// <summary>A gesture that never exceeded the tap threshold reads as a right-click tap (e.g. ability-cancel) instead of a drag-end -- a real drag's own end-of-gesture handling (e.g. MapWindow settling its smooth scroll onto the tile grid) has nothing to do for a tap anyway, since it never moved.</summary>
    private void HandleRightDragEnd()
    {
        if (_rightDragExceededTapThreshold)
        {
            _rightDragElement?.HandleRightDragEnd();
        }
        else
        {
            _rightDragElement?.HandleRightClickTap();
        }

        _rightDragElement = null;
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
    private void UpdateMouseWheelScroll(MouseState mouseState)
    {
        var wheelDelta = mouseState.ScrollWheelValue - _previousMouseState.ScrollWheelValue;
        if (wheelDelta == 0)
        {
            return;
        }

        var position = new Point(mouseState.X, mouseState.Y);
        var hoveredInteraction = TryHitTestInteraction(position);
        if (hoveredInteraction.Element is not { } element || FindScrollableAncestor(element) is not { } scrollableElement)
        {
            return;
        }

        var scrollDelta = -wheelDelta / WheelNotchValue * ScrollPixelsPerNotch;
        var isHorizontalOnly = scrollableElement.CanUserScrollHorizontal && !scrollableElement.CanUserScrollVertical;
        scrollableElement.ScrollBy(isHorizontalOnly ? new Vector2(scrollDelta, 0) : new Vector2(0, scrollDelta));
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

    /// <summary>Tooltip/User both sit structurally above DynamicHud, where every menu window lives today -- see the menu-mode branch of TryHitTestInteraction for why hit-testing still reaches them while menu mode is active.</summary>
    private static readonly UiLayer[] LayersAboveMenuMode = [UiLayer.User, UiLayer.Tooltip];

    /// <summary>User tier first, then DynamicHUD, then StaticHUD, then Base -- a higher tier can never lose to a lower one. Each tier topmost (last-raised) first. User is checked first purely for consistency (see the class's own doc comment) -- nothing placed there today is ever actually hit, since DragGhostContent's host window has no clickable content.</summary>
    private ElementInteraction TryHitTestInteraction(Point position)
    {
        if (_layers.IsMenuModeActive)
        {
            var menuInteraction = TryHitTestWhere(position, _layers.IsMenuWindow);
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

            return TryHitTestWhere(position, _layers.IsMenuModeExempt);
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

    /// <summary>Hit-tests every layer descending, topmost-drawn-first within each, but only elements matching predicate -- a non-matching sibling sharing the same layer (e.g. the Notification folder sitting alongside an open notification popup, both DynamicHud) is skipped entirely, never even hit-tested into its own children. Shared by TryHitTestInteraction's two menu-mode cases: UiLayerStack.IsMenuWindow (the open menu-window set) and UiLayerStack.IsMenuModeExempt (everything opted out of menu mode's blocking without itself being a menu window).</summary>
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
    /// subtree ends up drawn/hit-tested on top of its siblings at every level.
    /// </summary>
    private void RaiseToFront(Element element)
    {
        element.RaiseToFront();

        _layers.RaiseToFront(GetRootAncestor(element));
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
        // every keystroke (including e.g. MapWindow's WASD movement hotkeys) gets fed through
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
    /// _focusedElementAncestorChain) -- e.g. closing the quest-composer popup while its TextBox
    /// child holds focus: the popup is what actually calls Close(), the TextBox never does, so
    /// without the whole-chain subscription this would never fire at all and focus would be
    /// left dangling on a TextBox whose window is now hidden/pooled.
    /// </summary>
    private void OnFocusedElementClosed(Element closedElement) => RedirectFocusAwayFrom();

    /// <summary>
    /// A minimized element reads as "no longer the active thing", the same as a closed one --
    /// redirect focus the same way. Fires on every DisplayModeChanged, not just transitions
    /// into Minimized (restoring back out of it, or an unrelated Fixed/Fill change, also raise
    /// this event), so only the Minimized case is treated as a redirect trigger here. Active
    /// notification popups never hit this path -- NotificationMinimizeBehavior's "minimize"
    /// dismisses via a real Close() (see NotificationCenter.MinimizeNotification), not
    /// WindowDisplayMode.Minimized -- so OnFocusedElementClosed above is what actually covers
    /// the notification case task 1 asked for.
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
    /// (e.g. a future multi-TextBox form), or other DynamicHUD popups (e.g. the next active
    /// notification once the topmost one is dismissed) -- not the Base tier, whose windows
    /// (map/debug/selection) are fixed, distinct panels rather than a stack of equivalent ones;
    /// closing the quest-composer popup (the only Base window that can ever close) is meant to
    /// fall all the way through to _defaultFocusElement instead of grabbing some unrelated Base
    /// panel. Uses _focusedElementSiblings (snapshotted when this element gained focus, see
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
    /// StaticHUD element (Element.CanUserFocus -- e.g. the debug stats window opts out, see
    /// ShellBootstrapper), wrapping past either end. baseElements+staticHudElements only
    /// (map/debug/selection/health bar/quest trigger -- "fixed, distinct panels"), not
    /// dynamicHudElements or userElements: notifications are a separate tier dismissed via their
    /// own close/minimize button, not something a user tabs to, and User holds nothing
    /// focusable at all.
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
    private void RouteKeyPressesToFocusedElement(KeyboardState keyboardState)
    {
        if (_focusedElement is null)
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
    /// continuing to follow the mouse off-screen (or out of its parent's content area).
    /// </summary>
    private static Vector2 ClampMoveToBounds(Vector2 position, Vector2 size, Vector2 bounds) => new(
        MathHelper.Clamp(position.X, 0, MathHelper.Max(0, bounds.X - size.X)),
        MathHelper.Clamp(position.Y, 0, MathHelper.Max(0, bounds.Y - size.Y)));

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
    /// delta alone, but this method's own overflow correction runs afterward and previously had
    /// no floor of its own: dragging a window's left/top edge far enough past the screen's own
    /// edge (position going negative) shrank size by the overflow amount unconditionally, able to
    /// drive it to zero or negative with nothing to stop it. Confirmed bug, found via live
    /// testing: the Inventory window's item grid went blank after being resized this way -- cells
    /// were still being created correctly (see InventoryGridContent.RebuildCells), just inside a
    /// window whose own committed size had collapsed to zero height. Clamping size up to
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
    /// whatever the mouse happens to be over), otherwise a hover hit-test. The hover hit-test
    /// is skipped when the mouse hasn't moved since last frame -- it's a full recursive
    /// Rectangle.Contains walk over every element across all four tiers and their descendants
    /// (title buttons, tiled/floating children), which otherwise ran unconditionally every
    /// single frame regardless of whether the mouse was even moving. An element appearing/
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

        MouseCursor cursor;
        if (_activeInteraction.Kind == ElementDragInteractionKind.Resize)
        {
            cursor = GetResizeCursor(_activeInteraction.Edges);
            SetHoveredButton(null); // No hover feedback while a drag is actually in progress.
        }
        else if (_activeInteraction.Kind == ElementDragInteractionKind.Move)
        {
            cursor = MouseCursor.SizeAll;
            SetHoveredButton(null);
        }
        else if (IsContentDragBlockedAt(position))
        {
            cursor = MouseCursor.No;
            SetHoveredButton(null);
        }
        else if (position == previousPosition)
        {
            // Mouse hasn't moved -- reuse whatever cursor/hover state is already current rather
            // than re-walking the whole tree (see this method's own doc comment).
            cursor = CurrentCursor;
        }
        else
        {
            var interaction = TryHitTestInteraction(position);
            cursor = GetHoverCursor(interaction);
            SetHoveredButton(interaction.Button);
        }

        if (cursor != CurrentCursor)
        {
            MouseCursorEXT.SetCursor(cursor);
            CurrentCursor = cursor;
        }
    }

    /// <summary>
    /// True while the current content-drag's item belongs to some other entity's inventory (a
    /// corpse, say), not the player's own -- ResolveContentDrag refuses to bind it to a hotbar
    /// slot in that case, since a hotbar slot's whole premise is referencing one of the player's
    /// own stacks. False (permissive) whenever _playerQuery wasn't wired at all (e.g. a test
    /// harness with no IPlayerQuery) -- "unknown" is treated as "assume it's fine," the same
    /// convention IsTextInputFocused's own doc comment uses, rather than blocking every drag
    /// outright just because there's nothing to compare against.
    /// </summary>
    private bool IsDragFromNonPlayerInventory =>
        _playerQuery is not null && _contentDragOriginEntityId is { } originEntityId && originEntityId != _playerQuery.PlayerEntityId;

    /// <summary>True while dragging something that can never bind to a hotbar slot -- a Merged Stack cell (see _contentDragMergedItemDefinitionId's own doc comment) or an item from another entity's own inventory (see IsDragFromNonPlayerInventory) -- and position is currently over one. Checked ahead of the position == previousPosition shortcut so the cursor reads correctly for every frame of a stationary hover, not just the one it first arrived on.</summary>
    private bool IsContentDragBlockedAt(Point position)
    {
        if (_contentDragMergedItemDefinitionId is null && !IsDragFromNonPlayerInventory)
        {
            return false;
        }

        foreach (var element in _layers[UiLayer.StaticHud])
        {
            if (element is Window { Content: HotbarContent hotbarContent } && hotbarContent.TryGetSlotAt(position, out _))
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
