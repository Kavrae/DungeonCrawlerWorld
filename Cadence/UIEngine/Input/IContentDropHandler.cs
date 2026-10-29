using Microsoft.Xna.Framework;

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

    /// <summary>Every frame its ghost shows, the drag is held over dropTarget (what the nearest IContentDropTarget under the cursor named, null when none did) -- a target may show whether it would take the drop.</summary>
    void OnDragOver(object payload, object? dropTarget, Point position) { }

    /// <summary>Whether payload can't be dropped on dropTarget at position (what the nearest IContentDropTarget under the cursor named, null when none did) -- the refusal cursor while held; on release, a drop any handler refuses is offered to none.</summary>
    bool IsDropRefusedAt(object payload, object? dropTarget, Point position) => false;

    /// <summary>Whether this handler claims a drop of payload at position on dropTarget (what the nearest IContentDropTarget there named, null when none did) -- claiming, whether or not anything changed, stops the handlers after it.</summary>
    bool TryDrop(object payload, object? dropTarget, Point position);
}
