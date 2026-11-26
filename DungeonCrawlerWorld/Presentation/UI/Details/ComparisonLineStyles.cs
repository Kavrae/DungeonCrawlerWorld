using Game.Modules.Actions;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.UI.Details;

/// <summary>How a details window colours its lines and shape tiles against the items or actions it is compared with: Item Details' and Action Details' one rule.</summary>
/// <remarks>
/// Fill it with every compared definition (Add, each as the player would use it), then ask Resolve per line and
/// ShapeHighlight per shape preview. Empty, every line keeps its own style and no tile is highlighted.
/// </remarks>
public sealed class ComparisonLineStyles(EffectTextNames names)
{
    private static readonly Color BodyTextColor = ActivatableDetailsSections.BodyTextColor;

    /// <summary>Advantage -- a better value, or a line some compared definition lacks.</summary>
    private static readonly Color BetterColor = ActivatableDetailsSections.BuffColor;

    private static readonly Color WorseColor = ActivatableDetailsSections.DebuffColor;

    private readonly List<IReadOnlyList<ItemComparisonStat>> _comparedStats = [];

    /// <summary>Each compared definition's targeting as the player would use it, at the same index as _comparedStats; null for one with no activator.</summary>
    private readonly List<TargetingSpec?> _comparedTargeting = [];

    /// <summary>Whether anything is compared against.</summary>
    public bool IsComparing => _comparedStats.Count > 0;

    public void Clear()
    {
        _comparedStats.Clear();
        _comparedTargeting.Clear();
    }

    /// <summary>Compares against definition, as preview says the player would use it (null: its own values).</summary>
    public void Add(ActivatableDefinition definition, ActivatablePreview? preview)
    {
        _comparedStats.Add(ItemComparisonStatExtraction.Extract(definition, names, preview));
        _comparedTargeting.Add(preview?.Targeting ?? definition.ActivatorOrNone?.Targeting);
    }

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

    /// <summary>
    /// Null (plain grid) unless every compared definition shares this exact Targeting.Shape. When they do, a tile is
    /// highlighted if it is in this shape's offsets but missing from at least one compared shape's -- Resolve's "not every
    /// other has it" rule at tile granularity. There is no worse case: a tile this shape lacks is never drawn.
    /// </summary>
    public HashSet<Point>? ShapeHighlight(TargetingSpec targeting, IReadOnlyList<Point> offsets)
    {
        if (!IsComparing)
        {
            return null;
        }

        var otherOffsetSets = new List<HashSet<Point>>(_comparedTargeting.Count);
        foreach (var otherTargeting in _comparedTargeting)
        {
            if (otherTargeting is null || otherTargeting.Shape != targeting.Shape)
            {
                return null;
            }

            otherOffsetSets.Add(new HashSet<Point>(TargetShapePreviewGeometry.ComputeOffsets(otherTargeting)));
        }

        var highlighted = new HashSet<Point>();
        foreach (var offset in offsets)
        {
            foreach (var otherOffsets in otherOffsetSets)
            {
                if (!otherOffsets.Contains(offset))
                {
                    highlighted.Add(offset);
                    break;
                }
            }
        }

        return highlighted;
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
