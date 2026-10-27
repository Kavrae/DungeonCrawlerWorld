using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>The content drag in progress as UiInputController last saw it: whether its ghost shows, what it carries, the size it was picked up at, and the cursor.</summary>
/// <remarks>Payload is whatever the IContentDragSource that started the drag named, null when nothing is dragged; only the content that draws the ghost and the drop handlers read it.</remarks>
public readonly record struct ContentDragState(bool Visible, object? Payload, Vector2 SourceSize, Point CursorPosition);

/// <summary>Where the pointer is and what it's dragging, as UiInputController last saw it, for content that draws at the cursor.</summary>
/// <remarks>UiInputController rewrites it at the end of every Update; built before it, so the contents that read it can take it in their constructors.</remarks>
public sealed class PointerState
{
    /// <summary>The mouse's screen position.</summary>
    public Point CursorPosition { get; internal set; }

    /// <summary>The content drag in progress, if any.</summary>
    public ContentDragState ContentDrag { get; internal set; }

    /// <summary>The topmost element under the cursor, null over nothing -- what the input handlers were handed this frame.</summary>
    public Element? HoveredElement { get; internal set; }

    /// <summary>Whether the topmost element under the cursor is element or one of its descendants.</summary>
    public bool IsOver(Element element)
    {
        for (var candidate = HoveredElement; candidate is not null; candidate = candidate.ParentElement)
        {
            if (ReferenceEquals(candidate, element))
            {
                return true;
            }
        }

        return false;
    }
}
