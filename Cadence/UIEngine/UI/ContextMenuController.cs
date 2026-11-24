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

    /// <summary>Opens options at the cursor for target, the element they act on (null for none), indenting the options under each heading when indentsUnderHeadings.</summary>
    /// <remarks>IsOpenFor(target) is true until the menu closes, however it closes: the target draws its highlight from it.</remarks>
    public void Open(Vector2 cursorPosition, IReadOnlyList<DropdownOption> options, Element? target = null, bool indentsUnderHeadings = false) => Dropdowns.OpenAt(cursorPosition, options, target, indentsUnderHeadings);

    /// <summary>Whether the open menu was opened for element, and element hasn't been closed since.</summary>
    public bool IsOpenFor(Element element) => Dropdowns.IsOpenFor(element);

    /// <summary>The element the open menu was opened for, while it hasn't been closed since; null otherwise.</summary>
    public Element? Target => Dropdowns.Target;

    /// <summary>Makes target what the open menu acts on in place of the element it was opened for: the same thing shown by a new element (a grid that rebuilt its cells). Nothing when no menu with a target is open.</summary>
    public void Retarget(Element target) => Dropdowns.Retarget(target);

    public void Close() => Dropdowns.Close();
}
