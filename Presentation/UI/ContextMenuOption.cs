namespace Presentation.UI;

/// <summary>
/// One row in a ContextMenu -- Label left-aligned, HotkeyText (if any) right-aligned, e.g.
/// ("Copy", "Ctrl+C", ...). Named Option, not Item, to avoid colliding with this codebase's
/// existing "item" vocabulary (inventory items, InventoryItemStackCell, ...) -- a context menu
/// entry is conceptually closer to a Windows-style menu command. IsHeader marks a non-interactive
/// section-label row instead (see Header, and ContextMenu.Show's own per-row-kind branch) --
/// build one with plain positional construction for an ordinary clickable option, or via Header
/// for a section label.
/// </summary>
public sealed record ContextMenuOption(string Label, string? HotkeyText, bool Enabled, Action OnSelect, bool IsHeader = false)
{
    /// <summary>
    /// A non-interactive section-label row -- e.g. an entity's name ahead of its own Loot/Inspect
    /// options (see MapWindow.TryOpenEntityContextMenuAt). Doubles as the visual separator
    /// between one contributor's group and the next in a stacked menu, so no separate blank-
    /// divider row concept is needed.
    /// </summary>
    public static ContextMenuOption Header(string text) => new(text, null, Enabled: false, OnSelect: static () => { }, IsHeader: true);

    /// <summary>The options choosing this one replaces the menu with, in place of running OnSelect -- a submenu. Null for an ordinary option.</summary>
    public IReadOnlyList<ContextMenuOption>? Submenu { get; init; }

    /// <summary>An option that opens submenu in the menu's place, marked as leading somewhere (see ContextMenu.SubmenuMarker). Disabled when submenu has nothing to choose.</summary>
    public static ContextMenuOption Opening(string label, IReadOnlyList<ContextMenuOption> submenu) =>
        new(label, ContextMenu.SubmenuMarker, Enabled: submenu.Count > 0, OnSelect: static () => { }) { Submenu = submenu };
}
