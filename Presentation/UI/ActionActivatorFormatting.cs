using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;

namespace Presentation.UI;

/// <summary>
/// Readable lines for an IActionActivator's Timing plus whatever extra field(s) are actually
/// meaningful for its concrete kind -- Targeting is deliberately excluded (see
/// TargetShapePreviewElement, the visual grid replacing plain "Targeting: ..." text). Each line
/// is only emitted when it's genuinely informative for that activator: a field that's null, zero,
/// or not applicable for a given kind is omitted outright rather than printed as "None"/"0" --
/// e.g. a Scroll never gets a Mana Cost line, a Potion never gets a Charges line.
/// </summary>
public static class ActionActivatorFormatting
{
    public static IReadOnlyList<string> BuildLines(IActionActivator activator, ActionCatalog actionCatalog)
    {
        var lines = new List<string> { $"Timing: {activator.Timing.Category}" };

        // ActionLockFrames null means "use the caster's own default lock," not "no lock" -- see
        // ActionTiming's own doc comment -- so omitting the line here (rather than printing
        // "Action Lock: none") is the honest reading, not a gap.
        if (activator.Timing.ActionLockFrames is { } actionLockFrames)
        {
            lines.Add($"Action Lock: {FormatSeconds(actionLockFrames)}");
        }

        if (activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            lines.Add($"Cooldown: {FormatSeconds(cooldownFrames)}");
        }

        switch (activator)
        {
            case ScrollActivator scroll:
                if (actionCatalog.TryGet(scroll.SpellId, out var spell))
                {
                    lines.Add($"Casts: {spell.Name}");
                }
                break;

            case WandActivator wand:
                lines.Add($"Charges: {wand.Charges}/{wand.MaxCharges}");
                break;

            case SpellActivator { ManaCost: > 0 } spellActivator:
                lines.Add($"Mana Cost: {spellActivator.ManaCost}");
                break;
        }

        return lines;
    }

    /// <summary>Ceiling(frames / GameTiming.FramesPerSecond) -- same rounding convention ModifierDisplayFormatting.FormatDuration/PotionCooldownEffects.RemainingSeconds already use.</summary>
    private static string FormatSeconds(ushort frames) => $"{(int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond)}s";
}
