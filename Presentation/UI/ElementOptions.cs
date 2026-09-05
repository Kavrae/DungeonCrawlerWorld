namespace Presentation.UI;

/// <summary>
/// Composes the independent option groups a UI element is built from.
/// </summary>
public sealed class ElementOptions
{
    public ElementHierarchyOptions? Hierarchy { get; set; }

    public ElementLayoutOptions? Layout { get; set; }

    public ElementChromeOptions? Chrome { get; set; }

    public ElementContentOptions? Content { get; set; }

    public TextOptions? Text { get; set; }

    public FolderOptions? Folder { get; set; }

    public ButtonOptions? Button { get; set; }
}