using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Reads what a button shows from its children, as a person reads it.</summary>
internal static class ButtonContent
{
    /// <summary>The text of the label Button.SetText gave button, or empty when it has none.</summary>
    public static string TextOf(Button button) =>
        button.ChildElements.OfType<Label>().FirstOrDefault(label => label.IsCentered)?.Text ?? string.Empty;

    /// <summary>The glyph of the icon Button.SetIcon gave button, or empty when it has none.</summary>
    public static string GlyphOf(Button button) =>
        button.ChildElements.OfType<IconElement>().FirstOrDefault()?.Glyph ?? string.Empty;
}
