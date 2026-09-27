namespace Presentation.UI;

/// <summary>Which piece of an element's scrollbars a point lands on.</summary>
public enum ScrollbarPart : byte
{
    None,
    VerticalTrack,
    VerticalThumb,
    HorizontalTrack,
    HorizontalThumb,
}

internal static class ScrollbarPartExtensions
{
    public static bool IsVertical(this ScrollbarPart part) => part is ScrollbarPart.VerticalTrack or ScrollbarPart.VerticalThumb;

    public static bool IsThumb(this ScrollbarPart part) => part is ScrollbarPart.VerticalThumb or ScrollbarPart.HorizontalThumb;

    public static bool IsTrack(this ScrollbarPart part) => part is ScrollbarPart.VerticalTrack or ScrollbarPart.HorizontalTrack;
}
