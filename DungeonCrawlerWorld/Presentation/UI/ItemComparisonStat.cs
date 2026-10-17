namespace Presentation.UI;

/// <summary>
/// One line of an item's own Effects/Activation section, as ItemComparisonStatExtraction.Extract
/// produces it -- the single source both plain single-item rendering and Item Details Comparison's
/// per-line coloring read from. Key identifies "the same kind of stat" across different items (e.g.
/// two different items' own "effect:statmod:Strength" entries are comparable to each other, a
/// "effect:damage" entry never is to a "effect:heal" one) -- see ItemComparisonStatExtraction for
/// the exact key scheme. ComparableValue is null for stats with no meaningful ranking (an enum like
/// Shape, or two Scrolls casting different named spells) -- HigherIsBetter is meaningless in that
/// case and should be ignored.
/// </summary>
public readonly record struct ItemComparisonStat(string Key, string DisplayText, double? ComparableValue, bool HigherIsBetter);
