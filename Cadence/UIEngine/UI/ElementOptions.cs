namespace UIEngine.UI;

/// <summary>
/// Composes the independent option groups a UI element is built from.
/// </summary>
public sealed class ElementOptions
{
    public ElementHierarchyOptions? Hierarchy { get; init; }

    public ElementLayoutOptions? Layout { get; init; }

    public ElementChromeOptions? Chrome { get; init; }

    public ElementContentOptions? Content { get; init; }

    public TextOptions? Text { get; init; }

    public FolderOptions? Folder { get; init; }

    public ButtonOptions? Button { get; init; }
}