namespace UIEngine.UI;

/// <summary>
/// One row in a Dropdown -- Label left-aligned, HotkeyText (if any) right-aligned, e.g.
/// ("Copy", "Ctrl+C", ...). Named Option, not Item: a context menu entry is a menu command, and
/// "item" means a game's items in a host. IsHeader marks a non-interactive
/// section-label row instead (see Header, and Dropdown.Show's own per-row-kind branch) --
/// build one with plain positional construction for an ordinary clickable option, or via Header
/// for a section label.
/// </summary>
public sealed record DropdownOption(string Label, string? HotkeyText, bool Enabled, Action OnSelect, bool IsHeader = false)
{
    /// <summary>
    /// A non-interactive section-label row -- e.g. a subject's name ahead of its own
    /// options. Doubles as the visual separator
    /// between one contributor's group and the next in a stacked menu, so no separate blank-
    /// divider row concept is needed.
    /// </summary>
    public static DropdownOption Header(string text) => new(text, null, Enabled: false, OnSelect: static () => { }, IsHeader: true);

    /// <summary>The options choosing this one replaces the menu with, in place of running OnSelect -- a submenu. Null for an ordinary option.</summary>
    public IReadOnlyList<DropdownOption>? Submenu { get; init; }

    /// <summary>An option that opens submenu in the menu's place, marked as leading somewhere (see Dropdown.SubmenuMarker). Disabled when submenu has nothing to choose.</summary>
    public static DropdownOption Opening(string label, IReadOnlyList<DropdownOption> submenu) =>
        new(label, Dropdown.SubmenuMarker, Enabled: submenu.Count > 0, OnSelect: static () => { }) { Submenu = submenu };
}
