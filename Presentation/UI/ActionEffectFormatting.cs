using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.StatModifiers;

namespace Presentation.UI;

/// <summary>
/// One readable line per IActionEffectEntry -- none of the concrete Effects types
/// (Game/Modules/Actions/Effects/) has its own ToString() today, so this is the single place that
/// turns each one into UI-facing text, replacing ItemDetailsWindow's own former placeholder
/// (GetType().Name per entry). ChainedEffect is the only recursive case -- its own trigger chance
/// plus every nested entry across every nested ActionEffect, collapsed onto the same one line
/// FormatEntry always returns (mirrors ItemDetailsWindow's own one-line-per-entry loop, so a
/// caller never has to know in advance whether an entry expands into more than one row).
/// </summary>
public static class ActionEffectFormatting
{
    public static string FormatEntry(IActionEffectEntry entry) => entry switch
    {
        DirectDamage damage => FormatDirectDamage(damage),
        DirectHeal heal => FormatDirectHeal(heal),
        DirectManaRestore mana => $"Restores {mana.Fraction:P0} of max mana",
        StatusEffectGrant status => $"Applies {status.StackCount} stack{(status.StackCount == 1 ? "" : "s")} of {status.Type}",
        StatusEffectImmunityGrant immunity => $"{immunity.Type} Immunity",
        StatModifierGrant modifier when IsDamageReduction(modifier) => FormatDamageReduction(modifier),
        StatModifierGrant modifier => FormatStatModifierGrant(modifier),
        AuraSourceGrant aura => FormatAuraSourceGrant(aura),
        HotkeyExpansionGrant expansion => $"Unlocks {expansion.Slots} additional hotkey slot{(expansion.Slots == 1 ? "" : "s")}",
        ChainedEffect chained => FormatChainedEffect(chained),
        _ => entry.GetType().Name,
    };

    private static string FormatDirectDamage(DirectDamage damage) =>
        damage.MinFlatDamage == damage.MaxFlatDamage
            ? $"Deals {damage.MinFlatDamage} damage"
            : $"Deals {damage.MinFlatDamage}-{damage.MaxFlatDamage} damage";

    private static string FormatDirectHeal(DirectHeal heal) =>
        heal.FlatAmount > 0
            ? $"Heals {heal.FlatAmount} + {heal.PercentOfMaxHealth:P0} of max health"
            : $"Heals {heal.PercentOfMaxHealth:P0} of max health";

    /// <summary>A Multiplicative, negative-magnitude IncomingDamage modifier -- the only shape "reduces damage taken" is currently granted in (e.g. ResistanceTestPotion) -- reads as "50%: x-0.5" under the generic Target/symbol/Magnitude format below, which is technically correct but not what a player wants to read. FormatDamageReduction below gives this one shape its own plain-English line instead.</summary>
    private static bool IsDamageReduction(StatModifierGrant modifier) =>
        modifier.Target == StatModifierTarget.IncomingDamage && modifier.Operation == StatModifierOperation.Multiplicative && modifier.Magnitude < 0;

    /// <summary>ConditionTag prefixed when present (e.g. "Fire Damage Reduction : 50%") -- unscoped IncomingDamage reductions (ConditionTag null) apply to every damage source, so the tag prefix would be misleading there.</summary>
    private static string FormatDamageReduction(StatModifierGrant modifier)
    {
        var tagPrefix = modifier.ConditionTag is { } tag ? $"{tag} " : string.Empty;
        return $"{tagPrefix}Damage Reduction : {-modifier.Magnitude:P0}";
    }

    /// <summary>Reuses StatModifierComponent.ToString()'s own +/-/x/÷ symbol convention for Operation x Polarity (Game/Modules/StatModifiers/Components/StatModifierComponent.cs) so an item's own preview reads consistently with the Ability Score window's live modifier list.</summary>
    private static string FormatStatModifierGrant(StatModifierGrant modifier)
    {
        var symbol = modifier.Operation == StatModifierOperation.Additive
            ? modifier.Polarity == StatModifierPolarity.Buff ? "+" : "-"
            : modifier.Polarity == StatModifierPolarity.Buff ? "x" : "÷";

        return $"{modifier.Target}: {symbol}{modifier.Magnitude} ({FormatDurationFrames(modifier.DurationFrames)})";
    }

    private static string FormatAuraSourceGrant(AuraSourceGrant aura) =>
        $"Grants a {aura.StatusEffectType} aura (radius {DistanceFalloff.MaxRadius(aura.AuraAndGlowStrength)}) -- {FormatDurationFrames(aura.DurationFrames)}";

    private static string FormatChainedEffect(ChainedEffect chained)
    {
        var nestedLines = new List<string>();
        foreach (var triggeredEffect in chained.TriggeredEffects)
        {
            foreach (var nestedEntry in triggeredEffect.Entries)
            {
                nestedLines.Add(FormatEntry(nestedEntry));
            }
        }

        return nestedLines.Count == 0
            ? $"{chained.TriggerChance:P0} chance to trigger a further effect"
            : $"{chained.TriggerChance:P0} chance to also: {string.Join("; ", nestedLines)}";
    }

    /// <summary>Same Ceiling(frames / GameTiming.FramesPerSecond) idiom ModifierDisplayFormatting.FormatDuration (Presentation/UI/ModifierDisplayLine.cs) already uses for the identical null-means-permanent StatModifierComponent duration field -- worded "for Ns" rather than "Ns remaining" since these lines describe what an effect *would* grant, not something already ticking down.</summary>
    private static string FormatDurationFrames(ushort? frames) =>
        frames is not { } value ? "Permanent" : $"for {(int)System.Math.Ceiling(value / (float)GameTiming.FramesPerSecond)}s";
}
