using Engine.Tiles;
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
using Presentation.UI.Details;
using Presentation.UI.Targeting;

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
    /// <summary>Every line a comparison compares, in simple mode -- the mode comparison always shows -- for an item or an action.</summary>
    /// <param name="preview">The item or action as the player would use it (Game.Views.ActivationPreviews); null shows its own values.</param>
    public static IReadOnlyList<ItemComparisonStat> Extract(ActivatableDefinition definition, EffectTextNames names, ActivatablePreview? preview = null)
    {
        var stats = new List<ItemComparisonStat>(ExtractEffectStats(definition, names, DetailsMode.Simple, preview));
        stats.AddRange(ExtractCostStats(definition, names, preview));

        if (definition.ActivatorOrNone is { } activator)
        {
            stats.AddRange(ExtractActivatorStats(activator, preview, DetailsMode.Simple, names));
            if (definition.Toggle is null)
            {
                stats.AddRange(ExtractTargetingStats(activator, preview, DetailsMode.Simple, names));
            }

            if (ActivatableDetailsSections.AurasPlacedBy(definition, preview) is [var measuredAura, ..])
            {
                stats.Add(AuraRadiusStat(measuredAura));
            }

            stats.AddRange(ExtractResourceStats(activator));
        }

        return stats;
    }

    /// <summary>
    /// The Cost section's lines, one per entry: what using it takes ("To use: ...", "To turn on: ..." for a toggle), then
    /// what a toggle takes while on ("While on, every 1s: ..."), each as the player would pay it when previewed. A
    /// resource drain compares only with a drain of the same resource at the same point (its Key names both), lower being
    /// better and none best; anything else a use takes is shown but never ranked.
    /// </summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractCostStats(ActivatableDefinition definition, EffectTextNames names, ActivatablePreview? preview = null)
    {
        var stats = new List<ItemComparisonStat>();
        AddCostStats(stats, definition.ActivationEffects, definition.Toggle is null ? "To use" : "To turn on", "use", names, preview);
        if (definition.Toggle?.Periodic is { } periodic)
        {
            AddCostStats(stats, periodic.Effects, $"While on, every {TimingText.Seconds(periodic.IntervalFrames)}", "upkeep", names, preview);
        }

        return stats;
    }

    private static void AddCostStats(List<ItemComparisonStat> stats, IReadOnlyList<Effect> effects, string prefix, string keyPoint, EffectTextNames names, ActivatablePreview? preview)
    {
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                var text = $"{prefix}: {EffectFormatting.FormatEntry(entry, names, DetailsMode.Simple, preview?.Entries)}";
                if (entry is IResourceDrain drain)
                {
                    var amount = preview?.Of(entry)?.ValueOr(EffectAmount.ResourceDrained, drain.Amount) ?? drain.Amount;
                    stats.Add(new ItemComparisonStat($"cost:{keyPoint}:{drain.Resource}", text, amount, HigherIsBetter: false, ValueWhenAbsent: 0));
                    continue;
                }

                stats.Add(new ItemComparisonStat($"cost:{keyPoint}:{entry.GetType().Name}", text, null, HigherIsBetter: false, ValueWhenAbsent: 0));
            }
        }
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
    /// Simple shows the timing as a word on its own only when it isn't Immediate; Advanced shows "Timing: X" with its
    /// tooltip. Targeting is never a line: it is the schematic's labels (ExtractTargetingStats).
    /// </param>
    public static IReadOnlyList<ItemComparisonStat> ExtractActivatorStats(IActionActivator activator, ActivatablePreview? preview = null, DetailsMode mode = DetailsMode.Advanced, EffectTextNames? names = null)
    {
        var stats = new List<ItemComparisonStat>();
        var category = activator.Timing.Category;

        if (mode == DetailsMode.Advanced)
        {
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
            stats.Add(category == ActionTimingCategory.Delayed
                ? new ItemComparisonStat("activator:windup", $"Windup: {FormatSeconds(actionLockFrames)}", actionLockFrames, HigherIsBetter: false, Unit: StatUnit.Frames)
                : new ItemComparisonStat("activator:actionLock", $"Action Lock: {FormatSeconds(actionLockFrames)}", actionLockFrames, HigherIsBetter: false, Unit: StatUnit.Frames));
        }

        if (activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            stats.Add(CooldownStat(cooldownFrames, FormatSeconds(cooldownFrames), tooltip: null));
        }
        else if (activator.Timing.CooldownMatchesActionLock)
        {
            var tooltip = mode == DetailsMode.Advanced ? CooldownMatchesActionLockTooltip : null;
            stats.Add((preview?.ActionLockFrames ?? activator.Timing.ActionLockFrames) is { } lockFrames
                ? CooldownStat(lockFrames, TimingText.Seconds(lockFrames), tooltip)
                : new ItemComparisonStat("activator:cooldown", "Cooldown: your action lock", null, HigherIsBetter: false, tooltip, ValueWhenAbsent: 0));
        }

        switch (activator)
        {
            case ScrollActivator scroll:
                stats.Add(new ItemComparisonStat("activator:casts", $"Casts: {scroll.Spell.Name}", null, HigherIsBetter: true));
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

    /// <summary>The target schematic's labels, as used when previewed: its range ("10") when it has a path, and its shape's size ("r 3") when the shape has one.</summary>
    /// <param name="mode">Advanced adds a scaled value's change and its breakdown to the tooltip, which otherwise names the value ("Range: 10", "Circle radius: 3").</param>
    public static IReadOnlyList<ItemComparisonStat> ExtractTargetingStats(IActionActivator activator, ActivatablePreview? preview = null, DetailsMode mode = DetailsMode.Advanced, EffectTextNames? names = null)
    {
        var declared = activator.Targeting;
        var targeting = preview?.Targeting ?? declared;
        var stats = new List<ItemComparisonStat>(2);
        if (TargetSchematicPlanner.ShowsRange(targeting))
        {
            stats.Add(TargetingStat(TargetingRangeKey, "Range", string.Empty, declared.MaximumRange, preview?.Range, mode, names ?? EffectTextNames.None));
        }

        if (TargetSchematicPlanner.AreaMeasureOf(targeting) is { } measure)
        {
            var splashes = measure == TargetSchematicAreaMeasure.Radius && targeting.TargetModeAffects == TargetModeAffects.MarkedOnly;
            var (label, prefix) = measure switch
            {
                TargetSchematicAreaMeasure.Radius when splashes => ("Splash radius", "Splash "),
                TargetSchematicAreaMeasure.Radius => ($"{targeting.Shape} radius", "r "),
                TargetSchematicAreaMeasure.Length => ($"{targeting.Shape} length", string.Empty),
                _ => ($"{targeting.Shape} width", string.Empty),
            };
            stats.Add(TargetingStat(TargetingAreaSizeKey, label, prefix, declared.AreaSize, preview?.AreaSize, mode, names ?? EffectTextNames.None));
        }

        if (targeting.Shape.HasFlag(TargetShape.Cone))
        {
            stats.Add(TargetingStat(TargetingKeyOf(TargetSchematicLabel.ConeWidth), "Cone width", string.Empty, declared.AreaSize, preview?.AreaSize, mode, names ?? EffectTextNames.None));
        }

        if (targeting.Shape.HasFlag(TargetShape.Ring))
        {
            var innerRadius = System.Math.Max(0, (int)(preview?.AreaSize?.Value ?? declared.AreaSize) - 1);
            stats.Add(new ItemComparisonStat(TargetingKeyOf(TargetSchematicLabel.InnerRadius), $"r {innerRadius}", null, HigherIsBetter: true, $"Ring inner radius: {innerRadius}"));
        }

        if (mode == DetailsMode.Advanced)
        {
            foreach (var callout in TargetSchematicPlanner.CalloutsOf(targeting))
            {
                stats.Add(CalloutStat(callout, targeting));
            }
        }

        return stats;
    }

    public const string TargetingRangeKey = "targeting:Range";

    public const string TargetingAreaSizeKey = "targeting:AreaSize";

    /// <summary>The label an aura's radius dimension shows: "r 4", its tooltip naming the aura.</summary>
    public static ItemComparisonStat AuraRadiusStat(TargetSchematicAura aura) =>
        new(TargetingKeyOf(TargetSchematicLabel.AuraRadius), $"r {aura.Size}", aura.Size, HigherIsBetter: true, $"{aura.Aura.Name} aura radius: {aura.Size}");

    /// <summary>The key of the targeting stat a schematic's label slot shows.</summary>
    public static string TargetingKeyOf(TargetSchematicLabel label) => $"targeting:{label}";

    /// <summary>An Advanced callout's text and its tooltip; a callout is never compared.</summary>
    private static ItemComparisonStat CalloutStat(TargetSchematicLabel callout, TargetingSpec targeting)
    {
        var (text, tooltip) = callout switch
        {
            TargetSchematicLabel.MinimumRange => ($"min {targeting.MinimumRange}", $"Minimum range: {targeting.MinimumRange}. It can't be aimed nearer than that."),
            TargetSchematicLabel.HitCount => HitCountText(targeting.ProjectileHitCount),
            TargetSchematicLabel.PathEnd => ("Set distance", "Unless something stops it, it flies on past the aimed tile to its full range."),
            TargetSchematicLabel.Walls when targeting.PathWalls == PathWallRule.BouncesOffWalls => ($"bounces ×{targeting.WallBounceCount}", $"It bounces off walls, up to {targeting.WallBounceCount} {(targeting.WallBounceCount == 1 ? "time" : "times")}."),
            TargetSchematicLabel.Walls => ("through walls", "It goes on through walls."),
            TargetSchematicLabel.AimSight => ("Required LoS", "Line of Sight to the targeted tile is required."),
            TargetSchematicLabel.ShapeSight => ("Blocked by walls", "Walls block it from reaching what they hide from where it lands."),
            _ => ("one target", "In Target mode it affects only the creature you mark."),
        };
        return new ItemComparisonStat(TargetingKeyOf(callout), text, null, HigherIsBetter: true, tooltip);
    }

    private static (string Text, string Tooltip) HitCountText(ushort hitCount) => hitCount switch
    {
        0 => ("hits none", "It passes over every creature on the way."),
        TargetingSpec.AllEntitiesOnPath => ("hits all", "Hits every target in its path"),
        _ => ($"hits {hitCount}", $"It hits the creatures on the way, stopping on the {Ordinal(hitCount)}."),
    };

    private static string Ordinal(int number) => (number % 100) switch
    {
        11 or 12 or 13 => $"{number}th",
        _ => (number % 10) switch
        {
            1 => $"{number}st",
            2 => $"{number}nd",
            3 => $"{number}rd",
            _ => $"{number}th",
        },
    };

    /// <summary>prefix and the value as used, named by label in its tooltip; in Advanced mode, its change and breakdown when it differs from declared.</summary>
    private static ItemComparisonStat TargetingStat(string key, string label, string prefix, int declared, PreviewedValue? asUsed, DetailsMode mode, EffectTextNames names)
    {
        var value = asUsed?.Value ?? declared;
        var stat = new ItemComparisonStat(key, $"{prefix}{value}", value, HigherIsBetter: true, $"{label}: {value}");
        if (asUsed is null || mode != DetailsMode.Advanced)
        {
            return stat;
        }

        ShownValue[] shown = [new ShownValue(label, asUsed, static amount => $"{amount:0.#}")];
        return PreviewedValueText.Change(shown) is var (changeText, isBuff)
            ? stat with { ChangeText = changeText, ChangeIsBuff = isBuff, TooltipText = $"{stat.TooltipText}\n{PreviewedValueText.Tooltip(shown, names)}" }
            : stat;
    }

    /// <summary>Advanced mode's tooltip over a cooldown that is as long as its user's action lock (ActionTiming.CooldownMatchesActionLock): Dodge's.</summary>
    public const string CooldownMatchesActionLockTooltip = "As long as your action lock";

    /// <summary>"Cooldown: 3s": lower is better, and none is best (ValueWhenAbsent 0).</summary>
    private static ItemComparisonStat CooldownStat(ushort frames, string seconds, string? tooltip) =>
        new("activator:cooldown", $"Cooldown: {seconds}", frames, HigherIsBetter: false, tooltip, Unit: StatUnit.Frames, ValueWhenAbsent: 0);

    /// <summary>Same Ceiling(frames / GameFrameRate.FramesPerSecond) idiom ActionActivatorFormatting/ModifierDisplayFormatting already use.</summary>
    private static string FormatSeconds(ushort frames) => $"{(int)System.Math.Ceiling(frames / (float)GameFrameRate.FramesPerSecond)}s";
}
