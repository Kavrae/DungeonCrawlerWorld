using Game;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions.Effects;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;

namespace Presentation.UI;

/// <summary>One readable line per IEffectEntry: the one place an effect entry becomes text a player reads.</summary>
/// <remarks>
/// Every built-in entry has a case; the type name is only a fallback for one that doesn't, and
/// EffectFormattingTests fails while any built-in content reaches it. Every line is a plain sentence, its duration last
/// ("Poison immunity for 5m"). Simple mode says what the entry does and its amounts; Advanced adds the body part it
/// lands on, and AdvancedQualifiers gives the lines Item Details shows under it ("Locked Duration").
/// ChainedEffect is the only recursive case: its trigger chance and every nested entry, on the one line every entry gets.
/// </remarks>
public static class EffectFormatting
{
    /// <param name="previews">Each entry's amounts as used (Game.Views.ActivationPreviews), shown in place of the entry's own; null, or an entry it doesn't hold, shows the entry's own amounts.</param>
    public static string FormatEntry(IEffectEntry entry, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews = null)
    {
        var amounts = new Amounts(previews?.GetValueOrDefault(entry));
        return entry switch
        {
            DirectDamage damage => FormatDirectDamage(damage, mode, amounts),
            DirectHeal heal => FormatDirectHeal(heal, mode, amounts),
            DirectManaRestore mana => $"Restores {amounts.Of(EffectAmount.ManaRestoredFraction, mana.Fraction):P0} of max mana",
            ManaDrain drain => $"Drains {FormatAmount(amounts.Of(EffectAmount.ResourceDrained, drain.Amount))} mana",
            HealthDrain drain => $"Drains {FormatAmount(amounts.Of(EffectAmount.ResourceDrained, drain.Amount))} health",
            StatusEffectGrant status => FormatStatusEffectGrant(status, names, mode, amounts),
            StatusEffectImmunityGrant immunity => WithDuration($"{StatusEffectTypeFacts.DisplayNameOf(immunity.Type)} immunity", amounts.Whole(EffectAmount.Duration, immunity.DurationFrames)),
            StatusEffectRemoval removal => $"Cures {StatusEffectTypeFacts.DisplayNameOf(removal.Type)}",
            ActivationDisableGrant disable => FormatActivationDisableGrant(disable, names, amounts),
            TerrainChange terrainChange => $"Turns the ground to {names.TerrainNameOf(terrainChange.TerrainKey)}",
            DodgeActivation => "Dodges most attacks with a step",
            StatModifierGrant modifier when IsDamageReduction(modifier) => FormatDamageReduction(modifier, names, amounts),
            StatModifierGrant modifier => FormatStatModifierGrant(modifier, names, amounts),
            AuraSourceGrant aura => FormatAuraSourceGrant(aura, amounts),
            HotkeyExpansionGrant expansion => $"Unlocks {expansion.Slots} additional hotkey slot{(expansion.Slots == 1 ? "" : "s")}",
            SpellGrant spell => $"Teaches {spell.Spell.Name}",
            ChainedEffect chained => FormatChainedEffect(chained, names, mode, previews, amounts),
            _ => entry.GetType().Name,
        };
    }

    /// <summary>An entry's amounts: as used when a preview holds them, otherwise the entry's own.</summary>
    private readonly record struct Amounts(EffectAmountPreview? Preview)
    {
        public float Of(EffectAmount amount, float own) => Preview?.ValueOr(amount, own) ?? own;

        public int? Whole(EffectAmount amount, int? own) => own is { } value ? (int)MathF.Round(Of(amount, (float)value)) : null;
    }

    /// <summary>A health or mana amount: whole numbers as they are, anything else to one decimal place.</summary>
    private static string FormatAmount(float amount) => $"{amount:0.#}";

    /// <summary>The text Item Details shows for an effect's "Locked Duration" line.</summary>
    public const string LockedDuration = "Locked Duration";

    /// <summary>The tooltip over a "Locked Duration" line.</summary>
    public const string LockedDurationTooltip = "This duration cannot be modified.";

    /// <summary>The lines Item Details shows under an entry in advanced mode, each with its tooltip, if any: "Locked Duration" for a duration nothing can change.</summary>
    public static IReadOnlyList<(string Text, string? Tooltip)> AdvancedQualifiers(IEffectEntry entry)
    {
        var lockedDuration = entry switch
        {
            StatModifierGrant modifier => modifier.FixedDuration && modifier.DurationFrames is not null,
            ActivationDisableGrant disable => disable.FixedDuration,
            _ => false,
        };

        return lockedDuration ? [(LockedDuration, LockedDurationTooltip)] : [];
    }

    /// <summary>frames as a player reads a length of time: seconds under a minute, then minutes, then hours, each rounded up ("90s" is "2m").</summary>
    public static string FormatDuration(int frames)
    {
        var seconds = (int)System.Math.Ceiling(frames / (float)GameFrameRate.FramesPerSecond);
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        var minutes = (int)System.Math.Ceiling(seconds / 60f);
        return minutes < 60 ? $"{minutes}m" : $"{(int)System.Math.Ceiling(minutes / 60f)}h";
    }

    private static string FormatDirectDamage(DirectDamage damage, DetailsMode mode, Amounts amounts)
    {
        var minimum = FormatAmount(amounts.Of(EffectAmount.DamageMinimum, damage.MinFlatDamage));
        var maximum = FormatAmount(amounts.Of(EffectAmount.DamageMaximum, damage.MaxFlatDamage));
        var flat = minimum == maximum ? minimum : $"{minimum}-{maximum}";
        var percentage = amounts.Of(EffectAmount.DamagePercentOfMaximumHealth, damage.PercentageDamage);
        var text = (damage.MaxFlatDamage, damage.PercentageDamage) switch
        {
            (0, > 0f) => $"Deals {percentage:P0} of max health as damage",
            (_, > 0f) => $"Deals {flat} damage + {percentage:P0} of max health",
            _ => $"Deals {flat} damage",
        };

        return mode == DetailsMode.Advanced ? $"{text} {BodyPartText(damage.BodyPart, damage.BodyPartTargetMode)}" : text;
    }

    /// <summary>"Heals 16 health", "Heals 50% of max health" or both -- the flat part as used includes the user's ability-score bonus, so a percentage heal can gain one.</summary>
    private static string FormatDirectHeal(DirectHeal heal, DetailsMode mode, Amounts amounts)
    {
        var flat = amounts.Of(EffectAmount.HealFlat, heal.FlatAmount);
        var percentage = amounts.Of(EffectAmount.HealPercentOfMaximumHealth, heal.PercentOfMaxHealth);
        var text = (flat > 0, percentage > 0) switch
        {
            (true, true) => $"Heals {FormatAmount(flat)} + {percentage:P0} of max health",
            (true, false) => $"Heals {FormatAmount(flat)} health",
            _ => $"Heals {percentage:P0} of max health",
        };

        return mode == DetailsMode.Advanced ? $"{text} {BodyPartText(heal.BodyPart, heal.BodyPartTargetMode)}" : text;
    }

    /// <summary>Where damage or healing lands: the part an entry names, otherwise its mode's rule.</summary>
    private static string BodyPartText(BodyPartTargeting bodyPart, BodyPartTargetMode targetMode) =>
        BodyPartTargetingText(bodyPart, "to") ?? targetMode switch
        {
            BodyPartTargetMode.SingleTarget => "to a random body part",
            BodyPartTargetMode.LowestPercentage => "to the most hurt body part",
            _ => "split across every body part",
        };

    /// <summary>Where an entry that names a body part lands, after preposition ("to" an amount, "on" what is held), or null when it names none.</summary>
    private static string? BodyPartTargetingText(BodyPartTargeting bodyPart, string preposition) => bodyPart.Kind switch
    {
        BodyPartTargetingKind.GroundContact => $"{preposition} the body part touching the ground",
        BodyPartTargetingKind.Random => $"{preposition} a random body part",
        BodyPartTargetingKind.Type => $"{preposition} the {bodyPart.PartType.ToString().ToLowerInvariant()}",
        _ => null,
    };

    private static string FormatStatusEffectGrant(StatusEffectGrant status, EffectTextNames names, DetailsMode mode, Amounts amounts)
    {
        var effectName = StatusEffectTypeFacts.DisplayNameOf(status.Type);
        var stackCount = amounts.Whole(EffectAmount.Stacks, status.StackCount) ?? status.StackCount;
        var stacks = $"{stackCount} stack{(stackCount == 1 ? "" : "s")}";
        var text = status.Mode == StatusEffectGrantMode.TopUpTo
            ? $"Tops {effectName} up to {stacks}"
            : $"Applies {stacks} of {effectName}";

        if (mode == DetailsMode.Advanced && BodyPartTargetingText(status.BodyPart, "on") is { } bodyPartText)
        {
            text = $"{text} {bodyPartText}";
        }

        if (amounts.Whole(EffectAmount.Duration, status.DurationTicks) is not { } durationTicks)
        {
            return text;
        }

        return names.FramesPerDurationTickOf(status.Type) is { } framesPerTick
            ? WithDuration(text, durationTicks * framesPerTick)
            : $"{text} for {durationTicks} ticks";
    }

    /// <summary>"Prevents spells for 2s": every tag the query names, by its plural, joined with "and"; the tags it excludes after "except".</summary>
    private static string FormatActivationDisableGrant(ActivationDisableGrant disable, EffectTextNames names, Amounts amounts)
    {
        var prevented = new List<string>();
        foreach (var tag in disable.Query.RequireAll)
        {
            prevented.Add(names.PluralDisplayNameOf(tag).ToLowerInvariant());
        }

        foreach (var tag in disable.Query.RequireAny)
        {
            prevented.Add(names.PluralDisplayNameOf(tag).ToLowerInvariant());
        }

        var text = $"Prevents {JoinWithAnd(prevented)}";
        if (!disable.Query.Exclude.IsEmpty)
        {
            var excluded = new List<string>();
            foreach (var tag in disable.Query.Exclude)
            {
                excluded.Add(names.PluralDisplayNameOf(tag).ToLowerInvariant());
            }

            text += $" except {JoinWithAnd(excluded)}";
        }

        return WithDuration(text, amounts.Whole(EffectAmount.Duration, disable.DurationFrames));
    }

    private static string JoinWithAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}",
    };

    /// <summary>A Multiplicative, negative IncomingDamage modifier: "50% Fire Damage Reduction" rather than the generic "-50% Fire Damage Taken".</summary>
    private static bool IsDamageReduction(StatModifierGrant modifier) =>
        modifier.Target == StatModifierTarget.IncomingDamage && modifier.Operation == StatModifierOperation.Multiplicative && modifier.Magnitude < 0;

    /// <summary>"50% Fire Damage Reduction for 10m": ConditionTag named when present -- an unscoped reduction applies to every damage source, so naming one would mislead.</summary>
    private static string FormatDamageReduction(StatModifierGrant modifier, EffectTextNames names, Amounts amounts)
    {
        var tagPrefix = modifier.ConditionTag.IsNone ? string.Empty : $"{names.DisplayNameOf(modifier.ConditionTag)} ";
        return WithDuration($"{-modifier.Magnitude:P0} {tagPrefix}Damage Reduction", amounts.Whole(EffectAmount.Duration, modifier.DurationFrames));
    }

    /// <summary>"x2 Fire Damage Dealt for 2m": the amount (EffectAmountModifierText.FormatAmount), the condition tag before what it changes, then its duration.</summary>
    private static string FormatStatModifierGrant(StatModifierGrant modifier, EffectTextNames names, Amounts amounts)
    {
        var subject = EffectAmountModifierText.NameOf(modifier.Target);
        if (!modifier.ConditionTag.IsNone)
        {
            subject = $"{names.DisplayNameOf(modifier.ConditionTag)} {subject}";
        }

        var text = $"{EffectAmountModifierText.FormatAmount(modifier.Operation, modifier.Magnitude)} {subject}";
        return modifier.DurationFrames is null ? $"{text} permanently" : WithDuration(text, amounts.Whole(EffectAmount.Duration, modifier.DurationFrames));
    }

    /// <summary>"Grants a size 4 Poison aura with strength 16", wherever an aura grant is shown. No duration for a grant without one: the only such grant is a toggle's, which lasts while the toggle is on, not for good.</summary>
    private static string FormatAuraSourceGrant(AuraSourceGrant aura, Amounts amounts) =>
        WithDuration(
            $"Grants a size {amounts.Whole(EffectAmount.AuraSize, aura.Size)} {aura.Aura.Name} aura with strength {amounts.Whole(EffectAmount.AuraPower, aura.Power)}",
            amounts.Whole(EffectAmount.Duration, aura.DurationFrames));

    /// <summary>text, then "for 10m" when it has a duration -- a grant with none lasts while what holds it does, which its section already says.</summary>
    private static string WithDuration(string text, int? durationFrames) =>
        durationFrames is { } frames ? $"{text} {FormatForDuration(frames)}" : text;

    private static string FormatChainedEffect(ChainedEffect chained, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews, Amounts amounts)
    {
        var nestedLines = new List<string>();
        foreach (var triggeredEffect in chained.TriggeredEffects)
        {
            foreach (var nestedEntry in triggeredEffect.Entries)
            {
                nestedLines.Add(FormatEntry(nestedEntry, names, mode, previews));
            }
        }

        var chance = amounts.Of(EffectAmount.ProcChance, chained.TriggerChance);
        return nestedLines.Count == 0
            ? $"{chance:P0} chance to trigger a further effect"
            : $"{chance:P0} chance to also: {string.Join("; ", nestedLines)}";
    }

    /// <summary>"for 10m" -- these lines describe what an effect would grant, not something already counting down.</summary>
    private static string FormatForDuration(int frames) => $"for {FormatDuration(frames)}";
}
