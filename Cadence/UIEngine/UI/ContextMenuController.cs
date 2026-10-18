using Microsoft.Xna.Framework;

namespace UIEngine.UI;

/// <summary>
/// A right-click's context menu: the shared Dropdown opened at the cursor. Only the gesture is here; what options appear
/// is up to whoever detects the right-click (TextBox offers Cut/Copy/Paste/Select All; see DropdownOption, and
/// DynamicHudContextMenus for the shared Close/Close All), and the popup and its rules are DropdownController's.
/// </summary>
public sealed class ContextMenuController(DropdownController dropdowns)
{
    /// <summary>The dropdown every context menu opens in -- also the one a DropdownField opens, so only one is ever open.</summary>
    public DropdownController Dropdowns { get; } = dropdowns;

    public bool IsOpen => Dropdowns.IsOpen;

    /// <summary>The live popup itself.</summary>
    internal Element Menu => Dropdowns.Menu;

    public void Open(Vector2 cursorPosition, IReadOnlyList<DropdownOption> options) => Dropdowns.OpenAt(cursorPosition, options);

    public void Close() => Dropdowns.Close();
}
