using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;

namespace Presentation.UI;

/// <summary>
/// One ItemComparisonStat per line ItemDetailsWindow's Effects/Activation sections show -- the
/// single source both plain single-item rendering and Item Details Comparison's own per-line
/// coloring read from, so the two can never drift out of sync about what lines exist or what they
/// say. Reuses ActionEffectFormatting.FormatEntry for each effect entry's own DisplayText (the
/// same text a non-compared item already showed before this existed); activator lines are built
/// directly here rather than through ActionActivatorFormatting.BuildLines, since that method
/// returns one flat string list with no per-field Key to key comparisons off of.
/// </summary>
public static class ItemComparisonStatExtraction
{
    public static IReadOnlyList<ItemComparisonStat> Extract(ItemDefinition definition, ActionCatalog actionCatalog)
    {
        var stats = new List<ItemComparisonStat>(ExtractEffectStats(definition));

        if (definition.Activator is { } activator)
        {
            stats.AddRange(ExtractActivatorStats(activator, actionCatalog));
        }

        return stats;
    }

    /// <summary>Just the Effects-section half -- ItemDetailsWindow.BuildEffectsSection's own line source, since Effects and Activation render as separate sections with their own headers.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractEffectStats(ItemDefinition definition)
    {
        var stats = new List<ItemComparisonStat>();

        foreach (var effect in definition.Effects)
        {
            foreach (var entry in effect.Entries)
            {
                stats.Add(ExtractEffectStat(entry));
            }
        }

        return stats;
    }

    private static ItemComparisonStat ExtractEffectStat(IActionEffectEntry entry)
    {
        var displayText = ActionEffectFormatting.FormatEntry(entry);

        return entry switch
        {
            DirectDamage damage => new ItemComparisonStat("effect:damage", displayText, (damage.MinAmount + damage.MaxAmount) / 2.0, HigherIsBetter: true),
            DirectHeal heal => new ItemComparisonStat("effect:heal", displayText, heal.Fraction * 100, HigherIsBetter: true),
            DirectManaRestore mana => new ItemComparisonStat("effect:manaRestore", displayText, mana.Fraction * 100, HigherIsBetter: true),
            StatusEffectGrant status => new ItemComparisonStat($"effect:status:{status.Type}", displayText, status.StackCount, HigherIsBetter: true),
            // Signed by Polarity regardless of Operation (Additive/Multiplicative) -- a simplification: this value only drives green/red ranking, not the actual applied math (see StatModifierGrant.Apply for that), so a buff always ranks "higher magnitude is better" and a debuff the opposite.
            StatModifierGrant modifier => new ItemComparisonStat($"effect:statmod:{modifier.Target}", displayText, modifier.Polarity == StatModifierPolarity.Buff ? modifier.Magnitude : -modifier.Magnitude, HigherIsBetter: true),
            AuraSourceGrant aura => new ItemComparisonStat($"effect:aura:{aura.StatusEffectType}", displayText, DistanceFalloff.MaxRadius(aura.AuraAndGlowStrength), HigherIsBetter: true),
            HotkeyExpansionGrant expansion => new ItemComparisonStat("effect:hotkeySlots", displayText, expansion.Slots, HigherIsBetter: true),
            ChainedEffect chained => new ItemComparisonStat("effect:chained", displayText, chained.TriggerChance * 100, HigherIsBetter: true),
            _ => new ItemComparisonStat($"effect:{entry.GetType().Name}", displayText, null, HigherIsBetter: true),
        };
    }

    /// <summary>Just the Activation-section half -- ItemDetailsWindow.BuildActivationSection's own line source (targeting/timing/per-activator-type fields), excluding the shape-preview grid itself, which is a separate visual, not a text line.</summary>
    public static IReadOnlyList<ItemComparisonStat> ExtractActivatorStats(IActionActivator activator, ActionCatalog actionCatalog)
    {
        var stats = new List<ItemComparisonStat>();

        // Shape never gets a ComparableValue -- an enum has no "better/worse" direction (see
        // ItemDetailsWindow's own shape-preview grid for the separate, shape-match-gated tile
        // diff highlight that stands in for comparing shapes visually instead).
        stats.Add(new ItemComparisonStat("activator:shape", $"Shape: {activator.Targeting.Shape}", null, HigherIsBetter: true));
        stats.Add(new ItemComparisonStat("activator:range", $"Range: {activator.Targeting.Range}", activator.Targeting.Range, HigherIsBetter: true));

        if (activator.Targeting.AreaSize > 0)
        {
            stats.Add(new ItemComparisonStat("activator:areaSize", $"Area Size: {activator.Targeting.AreaSize}", activator.Targeting.AreaSize, HigherIsBetter: true));
        }

        stats.Add(new ItemComparisonStat("activator:timingCategory", $"Timing: {activator.Timing.Category}", null, HigherIsBetter: true));

        if (activator.Timing.ActionLockFrames is { } actionLockFrames)
        {
            stats.Add(new ItemComparisonStat("activator:actionLock", $"Action Lock: {FormatSeconds(actionLockFrames)}", actionLockFrames, HigherIsBetter: false));
        }

        if (activator.Timing.CooldownFrames is { } cooldownFrames)
        {
            stats.Add(new ItemComparisonStat("activator:cooldown", $"Cooldown: {FormatSeconds(cooldownFrames)}", cooldownFrames, HigherIsBetter: false));
        }

        switch (activator)
        {
            case ScrollActivator scroll:
                if (actionCatalog.TryGet(scroll.SpellId, out var spell))
                {
                    stats.Add(new ItemComparisonStat("activator:casts", $"Casts: {spell.Name}", null, HigherIsBetter: true));
                }
                break;

            case WandActivator wand:
                stats.Add(new ItemComparisonStat("activator:charges", $"Charges: {wand.Charges}/{wand.MaxCharges}", wand.MaxCharges, HigherIsBetter: true));
                break;

            case SpellActivator { ManaCost: > 0 } spellActivator:
                stats.Add(new ItemComparisonStat("activator:manaCost", $"Mana Cost: {spellActivator.ManaCost}", spellActivator.ManaCost, HigherIsBetter: false));
                break;
        }

        return stats;
    }

    /// <summary>Same Ceiling(frames / GameTiming.FramesPerSecond) idiom ActionActivatorFormatting/ModifierDisplayFormatting already use.</summary>
    private static string FormatSeconds(ushort frames) => $"{(int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond)}s";
}
