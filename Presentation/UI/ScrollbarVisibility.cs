namespace Presentation.UI;

/// <summary>Whether a scrollable element draws scrollbars for the axes it can scroll.</summary>
public enum ScrollbarVisibility : byte
{
    /// <summary>A bar shows on an axis only while that axis is scrollable and its content overflows.</summary>
    Auto,

    /// <summary>Never draws a bar or reserves space for one; the element still scrolls with the mouse wheel.</summary>
    Hidden,
}
