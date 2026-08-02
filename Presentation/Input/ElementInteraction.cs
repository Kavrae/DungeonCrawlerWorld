using Presentation.UI;

namespace Presentation.Input;

/// <summary>What kind of drag interaction (if any) a press on an element starts -- see ElementInteraction.</summary>
internal enum ElementDragInteractionKind
{
    None,
    Move,
    Resize,
}

/// <summary>
/// The result of Element.TryHitTestInteraction: which element (if any) was hit, what drag
/// interaction (if any) it starts, and -- for a button hit -- which button, so
/// GameInputController can track press/release state (Window Chrome Phase A0/B) from the same
/// hit-test that also drives raise-to-front and Move/Resize (Phase A1/C/D). Window is null
/// only when nothing was hit at all; it's still set (with Kind None) for a plain button/title/
/// content click, since that still needs to raise the window to front.
/// </summary>
internal readonly record struct ElementInteraction(ElementDragInteractionKind Kind, Element? Element, ResizeEdges Edges, Button? Button)
{
    public static readonly ElementInteraction NotHit = new(ElementDragInteractionKind.None, null, ResizeEdges.None, null);

    public static ElementInteraction ButtonClick(Element element, Button button) => new(ElementDragInteractionKind.None, element, ResizeEdges.None, button);

    public static ElementInteraction Click(Element element) => new(ElementDragInteractionKind.None, element, ResizeEdges.None, null);

    public static ElementInteraction Move(Element element) => new(ElementDragInteractionKind.Move, element, ResizeEdges.None, null);

    public static ElementInteraction Resize(Element element, ResizeEdges edges) => new(ElementDragInteractionKind.Resize, element, edges, null);
}
