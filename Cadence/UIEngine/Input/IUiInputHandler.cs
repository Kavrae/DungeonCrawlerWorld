using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>What an application does with raw input beyond the window framework's own routing -- its hotkeys, presses it intercepts, and where its controls refuse input.</summary>
/// <remarks>
/// UiInputController calls every registered handler at fixed points of its Update, in registration order; each member
/// does nothing by default. hoveredElement is what the controller's own hit test found under the cursor (topmost, menu
/// mode respected), re-tested only when the cursor or what's under it may have changed -- a handler asks it rather than
/// scanning layers. releasedElement is the same hit test at the release, made after the click resolved.
/// </remarks>
public interface IUiInputHandler
{
    /// <summary>Every frame, after focus cycling and Escape and before the focused element's key presses.</summary>
    void HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused) { }

    /// <summary>A left press at position, before anything is hit-tested or focused -- true consumes it, so nothing else sees the press.</summary>
    bool TryConsumePress(Point position) => false;

    /// <summary>A left press that wasn't consumed, after the pressed element was raised and focused and any content drag captured.</summary>
    void OnPress(Element? pressedElement, Point position) { }

    /// <summary>A left release at position over releasedElement, after the click and any content drop were resolved -- isTap is false once the press has become a drag, even if it came back to where it started.</summary>
    void OnRelease(Element? releasedElement, Point position, bool isTap) { }

    /// <summary>A right press at position over pressedElement, after an open context menu was dismissed; not heard when the press dismissed a light-dismissable.</summary>
    void OnRightPress(Element? pressedElement, Point position) { }

    /// <summary>Every frame, after the cursor is chosen -- isInteractionInProgress is a window move, resize or scrollbar drag, during which hoveredElement isn't refreshed.</summary>
    void OnPointerUpdate(Element? hoveredElement, Point position, bool isInteractionInProgress) { }

    /// <summary>Whether the cursor at position, over hoveredElement, shows that what's there refuses to be used right now -- checked every frame, even under a still mouse.</summary>
    bool IsInputRefusedAt(Element? hoveredElement, Point position) => false;
}
