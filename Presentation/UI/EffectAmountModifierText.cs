using Game.Modules.StatModifiers;

namespace Presentation.UI;

/// <summary>Player-facing wording for a modifier on an amount an effect carries (EffectModifiers): mana restored and drained, status stacks, proc chance, aura power and size.</summary>
internal static class EffectAmountModifierText
{
    /// <summary>What the modifier changes, as a player reads it, or null for a target that isn't an effect amount.</summary>
    public static string? SubjectOf(StatModifierTarget target) => target switch
    {
        StatModifierTarget.OutgoingManaRestore => "Mana Restoration Given",
        StatModifierTarget.IncomingManaRestore => "Mana Restoration Received",
        StatModifierTarget.OutgoingManaDrain => "Mana Drain Dealt",
        StatModifierTarget.IncomingManaDrain => "Mana Costs",
        StatModifierTarget.OutgoingHealthDrain => "Health Drain Dealt",
        StatModifierTarget.IncomingHealthDrain => "Health Costs",
        StatModifierTarget.OutgoingStatusStacks => "Status Effect Stacks",
        StatModifierTarget.IncomingStatusStacks => "Status Effect Stacks Received",
        StatModifierTarget.OutgoingProcChance => "Proc Chance",
        StatModifierTarget.IncomingProcChance => "Proc Chance Against You",
        StatModifierTarget.OutgoingAuraPower => "Aura Power",
        StatModifierTarget.IncomingAuraPower => "Aura Power Received",
        StatModifierTarget.OutgoingAuraSize => "Aura Size",
        StatModifierTarget.IncomingAuraSize => "Aura Size Received",
        _ => null,
    };

    /// <summary>The modifier's amount: a Multiplicative one that at least doubles as a multiplier ("x2"), any other Multiplicative one as a percentage ("-50%"), an Additive one as a signed amount ("+2").</summary>
    public static string FormatAmount(StatModifierOperation operation, float magnitude)
    {
        if (operation == StatModifierOperation.Additive)
        {
            return $"{(magnitude >= 0 ? "+" : "")}{magnitude:0.#}";
        }

        if (magnitude >= 1f)
        {
            return $"x{1f + magnitude:0.#}";
        }

        var percentage = (int)System.MathF.Round(magnitude * 100f);
        return $"{(percentage >= 0 ? "+" : "")}{percentage}%";
    }
}
