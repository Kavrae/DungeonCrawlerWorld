using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>One rule for what dropping a dragged payload means, registered with UiInputController.</summary>
/// <remarks>
/// UiInputController owns the gesture -- the press that captures a payload (IContentDragSource), the tap threshold, the
/// ghost's delay, and suppressing the origin's click once the drag is real. Handlers own the meaning: the controller tries
/// them in registration order on release until one claims the drop. Payloads are opaque to the controller; a handler
/// ignores any it doesn't recognise.
/// </remarks>
public interface IContentDropHandler
{
    /// <summary>False refuses the drag before it starts: nothing is picked up and the press is an ordinary click.</summary>
    bool AllowsDragStart(object payload) => true;

    /// <summary>The drag has been held long enough to show its ghost -- a drop target may start inviting a drop.</summary>
    void OnDragGhostShown(object payload) { }

    /// <summary>The drag has ended, dropped or not.</summary>
    void OnDragEnded(object payload) { }

    /// <summary>Whether the cursor at position, over hoveredElement (the controller's own hit test, resolved once a frame), shows that payload can't be dropped there.</summary>
    bool IsDropRefusedAt(object payload, Element? hoveredElement, Point position) => false;

    /// <summary>Whether this handler claims a drop of payload at position on dropTarget (the element hit there, if any) -- claiming, whether or not anything changed, stops the handlers after it.</summary>
    bool TryDrop(object payload, Element? dropTarget, Point position);
}
