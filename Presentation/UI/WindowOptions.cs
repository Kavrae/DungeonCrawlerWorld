namespace Presentation.UI;

/// <summary>
/// Composes the independent option groups a window is built from. Replaces a
/// single-inheritance options hierarchy (one subclass per window type, e.g. the old
/// TextWindowOptions : WindowOptions) with several small, independently-combinable pieces --
/// a window needing an extra axis of configuration (e.g. Text) sets that group directly
/// instead of requiring a new WindowOptions subclass. Any group left null falls back to
/// Window/TextWindow's own defaults.
/// </summary>
public sealed class WindowOptions
{
    public WindowHierarchyOptions? Hierarchy { get; set; }

    public WindowLayoutOptions? Layout { get; set; }

    public WindowChromeOptions? Chrome { get; set; }

    public WindowContentOptions? Content { get; set; }

    public TextOptions? Text { get; set; }
}
