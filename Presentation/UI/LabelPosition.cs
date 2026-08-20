namespace Presentation.UI;

/// <summary>
/// Where a control's label sits relative to its own primary visual (e.g. Toggle's checkbox
/// square) -- generic across any future control that needs a label positioned outside itself
/// rather than as a title or centered content text. Deliberately only the four cardinal
/// directions, not PopupAnchor's 8-way compass -- a diagonal doesn't mean anything for "which
/// side of this element does its label sit on," and admitting one would just be an ambiguous
/// value with no defined behavior.
/// </summary>
public enum LabelPosition
{
    North,
    South,
    East,
    West,
}
