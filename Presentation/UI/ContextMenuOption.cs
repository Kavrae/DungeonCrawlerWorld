namespace Presentation.UI;

/// <summary>
/// One row in a ContextMenu -- Label left-aligned, HotkeyText (if any) right-aligned, e.g.
/// ("Copy", "Ctrl+C", ...). Named Option, not Item, to avoid colliding with this codebase's
/// existing "item" vocabulary (inventory items, InventoryItemStackCell, ...) -- a context menu
/// entry is conceptually closer to a Windows-style menu command.
/// </summary>
public sealed record ContextMenuOption(string Label, string? HotkeyText, bool Enabled, Action OnSelect);
