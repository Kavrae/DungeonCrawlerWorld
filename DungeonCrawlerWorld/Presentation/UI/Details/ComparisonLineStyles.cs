using Game.Modules.Actions;
using Game.Views;
using Microsoft.Xna.Framework;

namespace Presentation.UI.Details;

/// <summary>How a details window colours its lines, the target schematic's labels among them, against the items or actions it is compared with: Item Details' and Action Details' one rule.</summary>
/// <remarks>
/// Fill it with every compared definition (Add, each as the player would use it), then ask Resolve per line. Empty, every
/// line keeps its own style.
/// </remarks>
public sealed class ComparisonLineStyles(EffectTextNames names)
{
    private static readonly Color BodyTextColor = ActivatableDetailsSections.BodyTextColor;

    /// <summary>Advantage -- a better value, or a line some compared definition lacks.</summary>
    private static readonly Color BetterColor = ActivatableDetailsSections.BuffColor;

    private static readonly Color WorseColor = ActivatableDetailsSections.DebuffColor;

    private readonly List<IReadOnlyList<ItemComparisonStat>> _comparedStats = [];

    /// <summary>Whether anything is compared against.</summary>
    public bool IsComparing => _comparedStats.Count > 0;

    public void Clear() => _comparedStats.Clear();

    /// <summary>Compares against definition, as preview says the player would use it (null: its own values).</summary>
    public void Add(ActivatableDefinition definition, ActivatablePreview? preview) =>
        _comparedStats.Add(ItemComparisonStatExtraction.Extract(definition, names, preview));

    /// <summary>
    /// A line's style: its own (its advanced-mode change, if any) with nothing compared. Better/Green when at least one
    /// compared definition lacks a line with the same Key -- an advantage marker, the whole line coloured -- unless the
    /// line says what lacking it is worth (ItemComparisonStat.ValueWhenAbsent: no cooldown or cost is 0), which is then
    /// compared like any other value, so a cooldown against none is worse. Otherwise, when
    /// every compared definition has the Key and every value (this line's included) is comparable, coloured by magnitude
    /// (ItemComparisonHighlighting), a Better line followed by its difference from the best of the others in green ("156
    /// (+53)"); ties and non-numeric shared lines stay plain.
    /// </summary>
    public LineStyle Resolve(ItemComparisonStat stat)
    {
        if (!IsComparing)
        {
            return LineStyle.Of(stat);
        }

        var otherValues = new List<double>();
        foreach (var otherStats in _comparedStats)
        {
            var found = false;
            foreach (var otherStat in otherStats)
            {
                if (otherStat.Key != stat.Key)
                {
                    continue;
                }

                found = true;
                if (otherStat.ComparableValue is { } otherValue)
                {
                    otherValues.Add(otherValue);
                }

                break;
            }

            if (found)
            {
                continue;
            }

            if (stat.ValueWhenAbsent is not { } absentValue)
            {
                return new LineStyle(BetterColor);
            }

            otherValues.Add(absentValue);
        }

        if (stat.ComparableValue is not { } ownValue)
        {
            return new LineStyle(BodyTextColor);
        }

        var bestOther = otherValues.Count == 0 ? (double?)null : stat.HigherIsBetter ? otherValues.Max() : otherValues.Min();
        otherValues.Add(ownValue);
        var highlight = ItemComparisonHighlighting.ComputeHighlight(ownValue, otherValues, stat.HigherIsBetter);
        return highlight switch
        {
            ComparisonHighlight.Better => new LineStyle(BetterColor, bestOther is { } best ? DifferenceText(ownValue - best, stat.Unit) : null, BetterColor),
            ComparisonHighlight.Worse => new LineStyle(WorseColor),
            _ => new LineStyle(BodyTextColor),
        };
    }

    /// <summary>A difference between two compared values, in their unit, in parentheses: "(+53)", "(+25%)", "(-3s)".</summary>
    public static string DifferenceText(double difference, StatUnit unit)
    {
        var sign = difference >= 0 ? "+" : "-";
        var magnitude = System.Math.Abs(difference);
        var text = unit switch
        {
            StatUnit.Percent => $"{magnitude:0.#}%",
            StatUnit.Frames => TimingText.Seconds((int)System.Math.Round(magnitude)),
            _ => $"{magnitude:0.#}",
        };

        return $"({sign}{text})";
    }
}
