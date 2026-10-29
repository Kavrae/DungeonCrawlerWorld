using Microsoft.Xna.Framework;

namespace UIEngine.Input;

/// <summary>Something a content drag can be dropped on -- an element, or what an element names as its DropTarget -- that names what a drop there lands on.</summary>
/// <remarks>
/// UiInputController asks the element under the cursor, then each ancestor, until one answers; the answer is opaque to it,
/// as a payload is, and goes to every IContentDropHandler. So a cell inside a grid inside a window answers for itself, the
/// grid answers for its empty space, and the window for its title bar.
/// </remarks>
public interface IContentDropTarget
{
    /// <summary>What a drop of payload at position lands on here, or null to leave it to an ancestor.</summary>
    object? DropTargetFor(object payload, Point position);
}
