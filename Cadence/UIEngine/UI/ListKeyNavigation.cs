using Microsoft.Xna.Framework.Input;

namespace UIEngine.UI;

/// <summary>Where a key moves a list's selection: Up and Down a row, Page Up and Page Down a page, Home and End to either end -- the one rule every list's keys follow.</summary>
/// <remarks>Counted over the rows that can be selected, in the order shown. From no selection, Down, Page Down and Home select the first row, Up, Page Up and End the last. A move stops at either end, never wrapping.</remarks>
public static class ListKeyNavigation
{
    /// <summary>Whether key is one of the list's moves; if so, targetIndex is the row it selects (selectedIndex itself at an end), -1 for an empty list.</summary>
    public static bool TryMove(Keys key, int selectedIndex, int count, int rowsPerPage, out int targetIndex)
    {
        targetIndex = -1;
        var step = key switch
        {
            Keys.Up => -1,
            Keys.Down => 1,
            Keys.PageUp => -System.Math.Max(1, rowsPerPage),
            Keys.PageDown => System.Math.Max(1, rowsPerPage),
            Keys.Home => int.MinValue,
            Keys.End => int.MaxValue,
            _ => 0,
        };
        if (step == 0)
        {
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        var last = count - 1;
        if (step is int.MinValue or int.MaxValue)
        {
            targetIndex = step == int.MinValue ? 0 : last;
            return true;
        }

        if (selectedIndex < 0 || selectedIndex > last)
        {
            targetIndex = step > 0 ? 0 : last;
            return true;
        }

        targetIndex = System.Math.Clamp(selectedIndex + step, 0, last);
        return true;
    }
}
