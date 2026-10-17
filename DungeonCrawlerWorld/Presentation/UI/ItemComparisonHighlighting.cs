namespace Presentation.UI;

/// <summary>Better = this stat's own extreme (max if higher-is-better, else min) among the values being compared; Worse = the opposite extreme; Normal = tied with everything, or strictly between the two extremes (only possible with 3+ values).</summary>
public enum ComparisonHighlight
{
    Normal,
    Better,
    Worse,
}

/// <summary>Pure magnitude comparison backing Item Details Comparison's per-line coloring -- see ItemComparisonStatExtraction for how a line's own ComparableValue/HigherIsBetter are derived, and ItemDetailsWindow for how this feeds into an actual TextColor.</summary>
public static class ItemComparisonHighlighting
{
    /// <summary>allValues must include ownValue itself (the caller's own value is one of the values being ranked, not compared against some separate set) and contain at least one value.</summary>
    public static ComparisonHighlight ComputeHighlight(double ownValue, IReadOnlyList<double> allValues, bool higherIsBetter)
    {
        var max = allValues[0];
        var min = allValues[0];
        foreach (var value in allValues)
        {
            max = System.Math.Max(max, value);
            min = System.Math.Min(min, value);
        }

        if (max == min)
        {
            return ComparisonHighlight.Normal;
        }

        var betterExtreme = higherIsBetter ? max : min;
        var worseExtreme = higherIsBetter ? min : max;

        if (ownValue == betterExtreme)
        {
            return ComparisonHighlight.Better;
        }

        return ownValue == worseExtreme ? ComparisonHighlight.Worse : ComparisonHighlight.Normal;
    }
}
