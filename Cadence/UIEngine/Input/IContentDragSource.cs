using Microsoft.Xna.Framework;

namespace UIEngine.Input;

/// <summary>What a content drag carries from the moment it starts: a payload only the drop handlers read, and the size its ghost is drawn at.</summary>
public readonly record struct ContentDragStart(object Payload, Vector2 SourceSize);

/// <summary>Something a left press can pick up and drag -- an element, or a window's content -- that names what it carries.</summary>
/// <remarks>UiInputController asks the pressed element, then the pressed window's content, on every left press. Every IContentDropHandler may still refuse the drag (AllowsDragStart) before it starts.</remarks>
public interface IContentDragSource
{
    /// <summary>Whether a press at pressPosition picks something up, and if so what.</summary>
    bool TryBeginContentDrag(Point pressPosition, out ContentDragStart start);
}
