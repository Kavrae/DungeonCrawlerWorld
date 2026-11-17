using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Reads a Dropdown's rows as a person reads them: each a Button or Toggle holding an optional icon, its label and its hotkey or count.</summary>
internal static class MenuRows
{
    /// <summary>The option label row shows: the label that fills it.</summary>
    public static string LabelOf(Element row) => row.ChildElements.OfType<Label>().First(label => label.FillsRemaining).Text;

    /// <summary>The hotkey, count or submenu marker at row's right end, or null for none.</summary>
    public static string? HotkeyOf(Element row) => row.ChildElements.OfType<Label>().FirstOrDefault(label => !label.FillsRemaining)?.Text;

    /// <summary>The glyph and colour of the icon at row's start, or null when no option in the menu has one.</summary>
    public static GlyphIcon? IconOf(Element row) =>
        row.ChildElements.OfType<IconElement>().FirstOrDefault() is { } icon ? new GlyphIcon(icon.Glyph, icon.GlyphColor) : null;

    /// <summary>The menu's headings, in order.</summary>
    public static IEnumerable<string> HeadingsOf(Dropdown menu) =>
        menu.ChildElements.OfType<Window>().Select(header => header.ChildElements.OfType<Label>().Single().Text);
}
