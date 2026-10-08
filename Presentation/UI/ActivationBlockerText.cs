using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>The player-facing words for why an action or item can't be used right now (ActivationBlocker), and the tooltip rows that show them.</summary>
/// <remarks>The melee text doesn't name or count parts: it becomes body-plan specific once actions declare the body parts that perform them.</remarks>
public static class ActivationBlockerText
{
    /// <summary>The same colour shop tooltips use for "Shop will not buy".</summary>
    public static readonly Color ReasonColor = Color.LightCoral;

    public static string Describe(ActivationBlocker blocker) => blocker switch
    {
        ActivationBlocker.NotActivatable => "Can't be activated",
        ActivationBlocker.MeleeDisabled => "No usable arms or hands",
        ActivationBlocker.NotEnoughMana => "Not enough mana",
        ActivationBlocker.NoManaPool => "Needs mana",
        ActivationBlocker.NoHealth => "Needs health",
        ActivationBlocker.NotEnoughHealth => "Not enough health",
        _ => string.Empty,
    };

    /// <summary>A divider and the reason, or nothing for None.</summary>
    public static void AppendRows(List<TooltipRow> rows, ActivationBlocker blocker)
    {
        if (blocker == ActivationBlocker.None)
        {
            return;
        }

        rows.Add(TooltipRow.Divider(WindowPalette.TitleTextColor));
        rows.Add(new TooltipRow(Describe(blocker), string.Empty, ReasonColor));
    }

    /// <summary>The rows AppendRows would add, or null for None.</summary>
    public static IReadOnlyList<TooltipRow>? RowsFor(ActivationBlocker blocker)
    {
        if (blocker == ActivationBlocker.None)
        {
            return null;
        }

        var rows = new List<TooltipRow>(2);
        AppendRows(rows, blocker);
        return rows;
    }
}
