using Game;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.Auras;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;
using Game.Views;

namespace Presentation.UI;

/// <summary>
/// One ItemComparisonStat per line ItemDetailsWindow's Effects/Activation sections show -- the
/// single source both plain single-item rendering and Item Details Comparison's own per-line
/// coloring read from, so the two can never drift out of sync about what lines exist or what they
/// say. Reuses EffectFormatting.FormatEntry for each effect entry's own DisplayText (the
/// same text a non-compared item already showed before this existed); activator lines are built
/// directly here rather than through ActionActivatorFormatting.BuildLines, since that method
/// returns one flat string list with no per-field Key to key comparisons off of.
/// </summary>
public static class ItemComparisonStatExtraction
{
    /// <summary>Every line Item Details Comparison compares, in simple mode -- the mode comparison always shows.</summary>
    /// <param name="preview">The item as the player would use it (Game.Views.ActivationPreviews); null shows its own values.</param>
    public static IReadOnlyList<ItemComparisonStat> Extract(ItemDefinition definition, ActionCatalog actionCatalog, EffectTextNames names, ActivatablePreview? preview = null)
    {
        var stats = new List<ItemComparisonStat>(ExtractEffectStats(definition, names, DetailsMode.Simple, preview));

        if (definition.Activator is { } activator)
        {
            stats.AddRange(ExtractActivatorStats(activator, actionCatalog, preview, DetailsMode.Simple, names));
            stats.AddRange(ExtractResourceStats(activator));
        }

        return stats;
    }

    /// <summary>Just the Effects-section half -- ItemDetailsWindow.BuildEffectsSection's own line source, since Effects and Activation render as separate sections with their own headers.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractEffectStats(ActivatableDefinition definition, EffectTextNames names, DetailsMode mode, ActivatablePreview? preview = null) =>
        ExtractEffectStats(definition.Effects, names, mode, preview?.Entries);

    /// <summary>One or more lines per entry of effects -- what an action or item holds, or an aura applies -- each with its amounts as previews holds them, when it does.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractEffectStats(IReadOnlyList<Effect> effects, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews = null)
    {
        var stats = new List<ItemComparisonStat>();

        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                AddEffectStats(stats, entry, names, mode, previews);
            }
        }

        return stats;
    }

    /// <summary>One entry's lines, as ExtractEffectStats gives them for each entry in turn.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractEntryStats(IEffectEntry entry, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews = null)
    {
        var stats = new List<ItemComparisonStat>();
        AddEffectStats(stats, entry, names, mode, previews);
        return stats;
    }

    /// <summary>One line per entry, then, in advanced mode, the entry's qualifier lines ("Locked Duration"), keyed under it.</summary>
    private static void AddEffectStats(List<ItemComparisonStat> stats, IEffectEntry entry, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews)
    {
        var stat = ExtractEffectStat(entry, names, mode, previews);

        if (mode != DetailsMode.Advanced)
        {
            stats.Add(stat);
            return;
        }

        if (previews?.GetValueOrDefault(entry) is { } amounts)
        {
            var shown = ShownValuesOf(entry, amounts, names);
            if (PreviewedValueText.Change(shown) is var (changeText, isBuff))
            {
                stat = stat with { ChangeText = changeText, ChangeIsBuff = isBuff, TooltipText = PreviewedValueText.Tooltip(shown, names) };
            }
        }

        stats.Add(stat);
        foreach (var (text, tooltip) in EffectFormatting.AdvancedQualifiers(entry))
        {
            stats.Add(new ItemComparisonStat($"{stat.Key}:{text}", text, null, HigherIsBetter: true, tooltip));
        }
    }

    /// <summary>Every amount entry's line shows, as its preview worked them out, each with its name and how one amount of it is written.</summary>
    private static IReadOnlyList<ShownValue> ShownValuesOf(IEffectEntry entry, EffectAmountPreview amounts, EffectTextNames names)
    {
        var framesPerDurationUnit = entry is StatusEffectGrant status ? names.FramesPerDurationTickOf(status.Type) : (ushort)1;
        var shown = new List<ShownValue>(amounts.Values.Count);
        foreach (var (amount, value) in amounts.Values)
        {
            shown.Add(new ShownValue(LabelOf(amount), value, FormatOf(amount, framesPerDurationUnit)));
        }

        return shown;
    }

    private static string LabelOf(EffectAmount amount) => amount switch
    {
        EffectAmount.DamageMinimum => "Lowest",
        EffectAmount.DamageMaximum => "Highest",
        EffectAmount.DamagePercentOfMaximumHealth or EffectAmount.HealPercentOfMaximumHealth => "Share of max health",
        EffectAmount.HealFlat => "Healing",
        EffectAmount.ManaRestoredFraction => "Share of max mana",
        EffectAmount.ResourceDrained => "Drained",
        EffectAmount.Stacks => "Stacks",
        EffectAmount.Duration => "Duration",
        EffectAmount.AuraPower => "Strength",
        EffectAmount.AuraSize => "Size",
        EffectAmount.ProcChance => "Chance",
        _ => amount.ToString(),
    };

    /// <summary>How one amount of amount is written; a duration counted in an effect's ticks of unknown length is written in ticks.</summary>
    private static Func<float, string> FormatOf(EffectAmount amount, ushort? framesPerDurationUnit) => amount switch
    {
        EffectAmount.DamagePercentOfMaximumHealth or EffectAmount.HealPercentOfMaximumHealth or EffectAmount.ManaRestoredFraction or EffectAmount.ProcChance =>
            static value => $"{value:P0}",
        EffectAmount.Duration when framesPerDurationUnit is { } framesPerUnit =>
            value => EffectFormatting.FormatDuration((int)MathF.Round(value * framesPerUnit)),
        EffectAmount.Duration => static value => $"{value:0} ticks",
        _ => static value => $"{value:0.#}",
    };

    private static ItemComparisonStat ExtractEffectStat(IEffectEntry entry, EffectTextNames names, DetailsMode mode, IReadOnlyDictionary<IEffectEntry, EffectAmountPreview>? previews)
    {
        var displayText = EffectFormatting.FormatEntry(entry, names, mode, previews);
        var amounts = previews?.GetValueOrDefault(entry);
        double AsUsed(EffectAmount amount, float own) => amounts?.ValueOr(amount, own) ?? own;

        return entry switch
        {
            DirectDamage damage => new ItemComparisonStat("effect:damage", displayText, (AsUsed(EffectAmount.DamageMinimum, damage.MinFlatDamage) + AsUsed(EffectAmount.DamageMaximum, damage.MaxFlatDamage)) / 2.0, HigherIsBetter: true),
            DirectHeal heal => new ItemComparisonStat("effect:heal", displayText, AsUsed(EffectAmount.HealPercentOfMaximumHealth, heal.PercentOfMaxHealth) * 100 + AsUsed(EffectAmount.HealFlat, heal.FlatAmount), HigherIsBetter: true, Unit: heal.FlatAmount > 0 ? StatUnit.Number : StatUnit.Percent),
            DirectManaRestore mana => new ItemComparisonStat("effect:manaRestore", displayText, AsUsed(EffectAmount.ManaRestoredFraction, mana.Fraction) * 100, HigherIsBetter: true, Unit: StatUnit.Percent),
            StatusEffectGrant status => new ItemComparisonStat($"effect:status:{status.Type}", displayText, AsUsed(EffectAmount.Stacks, status.StackCount), HigherIsBetter: true),
            // Signed by Polarity regardless of Operation (Additive/Multiplicative) -- a simplification: this value only drives green/red ranking, not the actual applied math (see StatModifierGrant.Apply for that), so a buff always ranks "higher magnitude is better" and a debuff the opposite.
            StatModifierGrant modifier => new ItemComparisonStat($"effect:statmod:{modifier.Target}", displayText, modifier.Polarity == StatModifierPolarity.Buff ? modifier.Magnitude : -modifier.Magnitude, HigherIsBetter: true),
            HotkeyExpansionGrant expansion => new ItemComparisonStat("effect:hotkeySlots", displayText, expansion.Slots, HigherIsBetter: true),
            ChainedEffect chained => new ItemComparisonStat("effect:chained", displayText, AsUsed(EffectAmount.ProcChance, chained.TriggerChance) * 100, HigherIsBetter: true, Unit: StatUnit.Percent),
            StatusEffectImmunityGrant immunity => new ItemComparisonStat($"effect:immunity:{immunity.Type}", displayText, immunity.DurationFrames is { } durationFrames ? AsUsed(EffectAmount.Duration, durationFrames) : null, HigherIsBetter: true, Unit: StatUnit.Frames),
            StatusEffectRemoval removal => new ItemComparisonStat($"effect:removal:{removal.Type}", displayText, null, HigherIsBetter: true),
            ActivationDisableGrant => new ItemComparisonStat("effect:disable", displayText, null, HigherIsBetter: true),
            TerrainChange terrainChange => new ItemComparisonStat($"effect:terrain:{terrainChange.TerrainKey}", displayText, null, HigherIsBetter: true),
            DodgeActivation => new ItemComparisonStat("effect:dodge", displayText, null, HigherIsBetter: true),
            AuraSourceGrant aura => new ItemComparisonStat($"effect:aura:{aura.Aura.Id}", displayText, AsUsed(EffectAmount.AuraPower, aura.Power), HigherIsBetter: true),
            _ => new ItemComparisonStat($"effect:{entry.GetType().Name}", displayText, null, HigherIsBetter: true),
        };
    }

    /// <summary>Just the Activation-section half -- ItemDetailsWindow.BuildActivationSection's own line source (targeting/timing/per-activator-type fields), excluding the shape-preview grid itself, which is a separate visual, not a text line.</summary>
    /// <param name="preview">The activation as the player would use it: a scroll's range and area with their Intelligence applied, and the lock its timing tooltip names. Null shows them as declared.</param>
    /// <param name="mode">
    /// Simple leaves out Shape, Range and Area Size, which the shape preview beside the lines shows, and shows the timing as a
    /// word on its own only when it isn't Immediate. Advanced shows every line, "Timing: X" with its tooltip, and the
    /// change and breakdown of a scaled range or area.
    /// </param>
    /// <param name="showsTargeting">False for a toggle: what it reaches is its aura's (BuildAuraSection), not its activator's self-targeting, so no Shape, Range or Area Size line.</param>
    public static IReadOnlyList<ItemComparisonStat> ExtractActivatorStats(IActionActivator activator, ActionCatalog actionCatalog, ActivatablePreview? preview = null, DetailsMode mode = DetailsMode.Advanced, EffectTextNames? names = null, bool showsTargeting = true)
    {
        var stats = new List<ItemComparisonStat>();
        var category = activator.Timing.Category;

        if (mode == DetailsMode.Advanced)
        {
            if (showsTargeting)
            {
                // Shape never gets a ComparableValue -- an enum has no "better/worse" direction (see
                // ItemDetailsWindow's own shape-preview grid for the separate, shape-match-gated tile
                // diff highlight that stands in for comparing shapes visually instead).
                stats.Add(new ItemComparisonStat("activator:shape", $"Shape: {activator.Targeting.Shape}", null, HigherIsBetter: true));
                stats.Add(TargetingStat("activator:range", "Range", activator.Targeting.AimRanges.MaximumRange, preview?.Range, names ?? EffectTextNames.None));

                if ((preview?.AreaSize?.Value ?? activator.Targeting.AreaSize) > 0)
                {
                    stats.Add(TargetingStat("activator:areaSize", "Area Size", activator.Targeting.AreaSize, preview?.AreaSize, names ?? EffectTextNames.None));
                }
            }

            stats.Add(new ItemComparisonStat("activator:timingCategory", $"Timing: {TimingText.Word(category)}", null, HigherIsBetter: true, TimingText.Tooltip(category, preview?.ActionLockFrames ?? activator.Timing.ActionLockFrames)));
        }
        else if (category != ActionTimingCategory.Immediate)
        {
            stats.Add(new ItemComparisonStat("activator:timingCategory", TimingText.Word(category), null, HigherIsBetter: true));
        }

        if (activator.Timing.ReleasesActionLock)
        {
            stats.Add(new ItemComparisonStat("activator:releasesActionLock", TimingText.ReleasesActionLock, null, HigherIsBetter: true));
        }

        if (activator.Timing.ActionLockFrames is { } actionLockFrames)
        {
            stats.Add(new ItemComparisonStat("activator:actionLock", $"Action Lock: {FormatSeconds(actionLockFrames)}", actionLockFrames, HigherIsBetter: false, Unit: StatUnit.Frames));
        }

        if (activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            stats.Add(new ItemComparisonStat("activator:cooldown", $"Cooldown: {FormatSeconds(cooldownFrames)}", cooldownFrames, HigherIsBetter: false, Unit: StatUnit.Frames));
        }

        switch (activator)
        {
            case ScrollActivator scroll:
                if (actionCatalog.TryGet(scroll.SpellId, out var spell))
                {
                    stats.Add(new ItemComparisonStat("activator:casts", $"Casts: {spell.Name}", null, HigherIsBetter: true));
                }
                break;

        }

        return stats;
    }

    /// <summary>The Resources section's lines: what using it spends that it holds itself -- a wand's charges, later ammunition. Empty for anything that holds none.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractResourceStats(IActionActivator activator) => activator switch
    {
        WandActivator wand => [new ItemComparisonStat("resource:charges", $"Charges: {wand.Charges}/{wand.MaxCharges}", wand.MaxCharges, HigherIsBetter: true)],
        _ => [],
    };

    /// <summary>"Range: 13", as used when previewed, with its change and breakdown when it differs from declared.</summary>
    private static ItemComparisonStat TargetingStat(string key, string label, int declared, PreviewedValue? asUsed, EffectTextNames names)
    {
        var value = asUsed?.Value ?? declared;
        var stat = new ItemComparisonStat(key, $"{label}: {value}", value, HigherIsBetter: true);
        if (asUsed is null)
        {
            return stat;
        }

        ShownValue[] shown = [new ShownValue(label, asUsed, static amount => $"{amount:0.#}")];
        return PreviewedValueText.Change(shown) is var (changeText, isBuff)
            ? stat with { ChangeText = changeText, ChangeIsBuff = isBuff, TooltipText = PreviewedValueText.Tooltip(shown, names) }
            : stat;
    }

    /// <summary>Same Ceiling(frames / GameFrameRate.FramesPerSecond) idiom ActionActivatorFormatting/ModifierDisplayFormatting already use.</summary>
    private static string FormatSeconds(ushort frames) => $"{(int)System.Math.Ceiling(frames / (float)GameFrameRate.FramesPerSecond)}s";
}
