namespace Presentation.UI.Chrome;

/// <summary>
/// Shared generic layout constants so windows read as one consistent UI instead of each
/// hardcoding its own padding/gap/separator sizes. Plain mutable fields, not const -- a future
/// data-driven theme loader needs to be able to overwrite them at startup.
/// </summary>
internal static class WindowChrome
{
    /// <summary>Generic inset between an element's own edges and its children's content area.</summary>
    public static float Padding = 4f;

    /// <summary>Generic space between two sibling controls.</summary>
    public static float Gap = 4f;

    /// <summary>Generic thickness of a thin divider/separator line.</summary>
    public static float SeparatorHeight = 1f;

    /// <summary>Width of a vertical scrollbar, height of a horizontal one, and the gutter either reserves.</summary>
    public static float ScrollbarThickness = 8f;

    /// <summary>Shortest a scrollbar thumb gets, however long the content is.</summary>
    public static float ScrollbarMinimumThumbLength = 16f;
}
