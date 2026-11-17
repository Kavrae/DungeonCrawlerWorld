using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>What kind of drag interaction (if any) a press on an element starts -- see ElementInteraction.</summary>
internal enum ElementDragInteractionKind : byte
{
    None,
    Move,
    Resize,
    ScrollThumb,
    ScrollTrack,
    PointerDrag,
}

/// <summary>
/// The result of Element.TryHitTestInteraction: which element (if any) was hit, what drag
/// interaction (if any) it starts, and -- for a hit on or inside a pressable control -- which control, so
/// UiInputController can track press/release state (Window Chrome Phase A0/B) from the same
/// hit-test that also drives raise-to-front and Move/Resize. Element is the deepest element hit (a label inside a
/// button, or the window owning a title button); Pressable is the nearest pressable control holding it. Element is null
/// only when nothing was hit at all; it is still set (with Kind None) for a plain button/title/
/// content click, since that still needs to raise the window to front.
/// </summary>
internal readonly record struct ElementInteraction(ElementDragInteractionKind Kind, Element? Element, ResizeEdges Edges, PressableElement? Pressable, ScrollbarPart ScrollbarPart)
{
    public static readonly ElementInteraction NotHit = new(ElementDragInteractionKind.None, null, ResizeEdges.None, null, ScrollbarPart.None);

    public static ElementInteraction PressableClick(Element element, PressableElement pressable) => new(ElementDragInteractionKind.None, element, ResizeEdges.None, pressable, ScrollbarPart.None);

    public static ElementInteraction Click(Element element) => new(ElementDragInteractionKind.None, element, ResizeEdges.None, null, ScrollbarPart.None);

    public static ElementInteraction Move(Element element) => new(ElementDragInteractionKind.Move, element, ResizeEdges.None, null, ScrollbarPart.None);

    public static ElementInteraction Resize(Element element, ResizeEdges edges) => new(ElementDragInteractionKind.Resize, element, edges, null, ScrollbarPart.None);

    public static ElementInteraction PointerDrag(Element element) => new(ElementDragInteractionKind.PointerDrag, element, ResizeEdges.None, null, ScrollbarPart.None);

    public static ElementInteraction Scrollbar(Element element, ScrollbarPart part) =>
        new(part.IsThumb() ? ElementDragInteractionKind.ScrollThumb : ElementDragInteractionKind.ScrollTrack, element, ResizeEdges.None, null, part);
}
