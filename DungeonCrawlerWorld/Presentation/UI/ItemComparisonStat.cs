namespace Presentation.UI;

/// <summary>What a stat line's ComparableValue counts in, for showing a difference between two of them.</summary>
public enum StatUnit : byte
{
    /// <summary>A plain number.</summary>
    Number,

    /// <summary>A percentage, already multiplied by 100.</summary>
    Percent,

    /// <summary>Simulation frames, shown as seconds.</summary>
    Frames,
}

/// <summary>
/// One line of an item's own Effects/Activation section, as ItemComparisonStatExtraction.Extract
/// produces it -- the single source both plain single-item rendering and Item Details Comparison's
/// per-line coloring read from. Key identifies "the same kind of stat" across different items (e.g.
/// two different items' own "effect:statmod:Strength" entries are comparable to each other, a
/// "effect:damage" entry never is to a "effect:heal" one) -- see ItemComparisonStatExtraction for
/// the exact key scheme. ComparableValue is null for stats with no meaningful ranking (an enum like
/// Shape, or two Scrolls casting different named spells) -- HigherIsBetter is meaningless in that
/// case and should be ignored. TooltipText, when set, is shown over the line.
/// </summary>
/// <param name="ChangeText">Advanced mode's total change from the line's base values, shown after it ("(+7)"); null when nothing changed them.</param>
/// <param name="ChangeIsBuff">Whether ChangeText is to the user's advantage (green) or not (red).</param>
/// <param name="Unit">What ComparableValue counts in.</param>
public readonly record struct ItemComparisonStat(
    string Key,
    string DisplayText,
    double? ComparableValue,
    bool HigherIsBetter,
    string? TooltipText = null,
    string? ChangeText = null,
    bool ChangeIsBuff = true,
    StatUnit Unit = StatUnit.Number);
