using Microsoft.Xna.Framework;

namespace Presentation.UI.Details;

/// <summary>How a details window draws one stat line: the text's colour, and an optional suffix after it in its own colour.</summary>
/// <param name="Suffix">Shown after the line: advanced mode's change ("(+7)"), or Item Details Comparison's difference on the better side.</param>
public readonly record struct LineStyle(Color TextColor, string? Suffix = null, Color SuffixColor = default)
{
    /// <summary>stat in body text, with its advanced-mode change, if any, green for the user's advantage and red against it.</summary>
    public static LineStyle Of(ItemComparisonStat stat) =>
        new(ActivatableDetailsSections.BodyTextColor, stat.ChangeText, stat.ChangeIsBuff ? ActivatableDetailsSections.BuffColor : ActivatableDetailsSections.DebuffColor);
}
