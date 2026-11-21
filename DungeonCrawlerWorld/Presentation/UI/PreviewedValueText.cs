using Game.Effects;
using Game.Modules.StatModifiers;

namespace Presentation.UI;

/// <summary>One value a line shows: what it is called on a line that shows several, its preview, and how one amount of it is written.</summary>
/// <param name="Format">Writes a non-negative amount of this value ("16", "50%", "2m").</param>
public readonly record struct ShownValue(string Label, PreviewedValue Value, Func<float, string> Format);

/// <summary>Advanced mode's text for values that differ from their base: the total change after a line ("(+7)") and the breakdown in its tooltip.</summary>
public static class PreviewedValueText
{
    /// <summary>The change after a line, in parentheses, and whether it is to the user's advantage -- null when no value on the line changed.</summary>
    /// <remarks>
    /// One changed value: its signed difference ("(+7)", "(-30s)"). Several: their one difference when they share it (a
    /// damage range raised by a flat bonus), else their one multiplier when they share that (a range doubled: "(x2)"),
    /// else each difference in turn. Every value is better when higher, so a rise is the user's advantage.
    /// </remarks>
    public static (string Text, bool IsBuff)? Change(IReadOnlyList<ShownValue> values)
    {
        var changed = Changed(values);
        if (changed.Count == 0)
        {
            return null;
        }

        var differences = changed.Select(static value => Signed(value.Value.Value - value.Value.Base, value.Format)).ToList();
        var isBuff = changed[0].Value.Value > changed[0].Value.Base;
        if (differences.Distinct().Count() == 1)
        {
            return ($"({differences[0]})", isBuff);
        }

        if (changed.All(static value => value.Value.Base > 0f))
        {
            var ratios = changed.Select(static value => MathF.Round(value.Value.Value / value.Value.Base, 2)).Distinct().ToList();
            if (ratios.Count == 1)
            {
                return ($"(x{ratios[0]:0.##})", isBuff);
            }
        }

        return ($"({string.Join(", ", differences)})", isBuff);
    }

    /// <summary>The tooltip over a line whose values changed: for each, "Base: 6", one line per step ("Intelligence: +13", "Draught of Venom (Item): x2"), "Total: 13" -- headed by its label when several changed. Null when none did.</summary>
    public static string? Tooltip(IReadOnlyList<ShownValue> values, EffectTextNames names)
    {
        var changed = Changed(values);
        if (changed.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var shown in changed)
        {
            if (changed.Count > 1)
            {
                lines.Add($"{shown.Label}:");
            }

            lines.Add($"Base: {shown.Format(shown.Value.Base)}");
            foreach (var contribution in shown.Value.Contributions)
            {
                lines.Add(ContributionText(contribution, names));
            }

            lines.Add($"Total: {shown.Format(shown.Value.Value)}");
        }

        return string.Join('\n', lines);
    }

    /// <summary>One step as a tooltip line: what made it, then what it did.</summary>
    public static string ContributionText(ValueContribution contribution, EffectTextNames names)
    {
        var amount = EffectAmountModifierText.FormatAmount(contribution.Operation, contribution.Amount);
        return contribution.Kind switch
        {
            ValueContributionKind.Modifier => $"{ModifierName(contribution, names)}: {amount}",
            ValueContributionKind.AbilityScore => $"{contribution.AbilityScore}: {amount}",
            ValueContributionKind.Strength => $"Aura strength: {amount}",
            ValueContributionKind.ScrollIntelligence => $"Intelligence (scroll): {amount}",
            _ => amount,
        };
    }

    private static string ModifierName(ValueContribution contribution, EffectTextNames names)
    {
        var name = contribution.Source is { } source && names.Sources is { } sources ? sources.Describe(source) : "Modifier";
        return contribution.ConditionTag.IsNone ? name : $"{name} ({names.DisplayNameOf(contribution.ConditionTag)})";
    }

    /// <summary>The values that changed, each base-and-result pair once: a damage range whose ends are equal is one value.</summary>
    private static List<ShownValue> Changed(IReadOnlyList<ShownValue> values) =>
        values.Where(static value => value.Value.IsModified).DistinctBy(static value => (value.Value.Base, value.Value.Value)).ToList();

    private static string Signed(float difference, Func<float, string> format) =>
        difference >= 0 ? $"+{format(difference)}" : $"-{format(-difference)}";
}
