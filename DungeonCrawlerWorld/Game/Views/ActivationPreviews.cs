using Engine.ECS.Systems;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Views;

/// <summary>An action's or item's amounts as they would be if one entity used it now, and its activator's targeting.</summary>
/// <param name="Targeting">The activator's targeting as it applies (TargetResolution.EffectiveSpec: a spell's scaled); null with no activator.</param>
/// <param name="Range">A spell's aim range (TargetingSpec.AimRanges) with the user's Intelligence applied (SpellScalingEffects); null for anything else, whose range is as declared.</param>
/// <param name="AreaSize">A spell's area, as Range.</param>
/// <param name="ActionLockFrames">How long using it locks the user, or winds up for a Delayed one: the activator's own lock, else the user's standard lock (StandardActionLockFrames); null with no activator.</param>
public sealed record ActivatablePreview(IReadOnlyDictionary<IEffectEntry, EffectAmountPreview> Entries, TargetingSpec? Targeting, PreviewedValue? Range, PreviewedValue? AreaSize, ushort? ActionLockFrames)
{
    /// <summary>entry's amounts as used, or null for an entry this preview doesn't hold.</summary>
    public EffectAmountPreview? Of(IEffectEntry entry) => Entries.GetValueOrDefault(entry);
}

/// <summary>Works out what the amounts actions, items and auras carry would be when used, each with the steps from its base, for details windows to show.</summary>
/// <remarks>
/// The user is the source and there is no target (EffectContext.NoTargetEntity): the user's Outgoing modifiers,
/// ability-score bonuses and spell scaling apply, and no target's Incoming modifiers do, so a number can differ from what
/// lands on a particular target. Every amount is worked out by the entry itself (IEffectEntry.PreviewAmounts) through the
/// helpers its Apply uses. Nothing is changed and nothing is rolled: a damage range stays a range, and a critical hit is
/// never included.
/// </remarks>
public sealed class ActivationPreviews(EffectServices effectServices, SimulationClock simulationClock)
{
    /// <summary>Every amount definition carries -- its effects, activation effects and a toggle's upkeep, nested ones included -- as userEntityId would use it now.</summary>
    public ActivatablePreview Preview(int userEntityId, ActivatableDefinition definition)
    {
        var activator = definition.ActivatorOrNone;
        var spellMultiplier = SpellScalingEffects.MultiplierFor(effectServices.AbilityScores, userEntityId, activator);
        var context = EffectContext.FromEntity(effectServices, userEntityId, EffectContext.NoTargetEntity, definition, simulationClock.CurrentFrame, spellMultiplier);

        var entries = new Dictionary<IEffectEntry, EffectAmountPreview>(ReferenceEqualityComparer.Instance);
        EffectEntries.ForEach(definition, entry => entries.TryAdd(entry, PreviewEntry(entry, context)));

        var actionLockFrames = activator is null
            ? (ushort?)null
            : StandardActionLockFrames.ResolveForEntity(effectServices.AbilityScores, effectServices.StatModifiers, userEntityId, activator.Timing.ActionLockFrames);

        if (activator is null || !SpellScalingEffects.Applies(activator))
        {
            return new ActivatablePreview(entries, activator?.Targeting, Range: null, AreaSize: null, actionLockFrames);
        }

        var scaled = SpellScalingEffects.ScaleTargeting(activator.Targeting, spellMultiplier);
        return new ActivatablePreview(
            entries,
            scaled,
            SpellValue(activator.Targeting.AimRanges.MaximumRange, scaled.AimRanges.MaximumRange, spellMultiplier),
            SpellValue(activator.Targeting.AreaSize, scaled.AreaSize, spellMultiplier),
            actionLockFrames);
    }

    /// <summary>The amounts aura's effects carry where its power is power, as one of its ticks applies them: no source entity, the amounts multiplied by its strength (AuraDefinition.MagnitudeAt).</summary>
    public IReadOnlyDictionary<IEffectEntry, EffectAmountPreview> PreviewAura(AuraDefinition aura, float power)
    {
        var context = new EffectContext(effectServices, ActionSource.FromAura(effectServices.AuraSources.GetId(aura)), SourceEntityId: null, EffectContext.NoTargetEntity, aura.EffectName, aura.Tags, simulationClock.CurrentFrame)
        {
            Magnitude = aura.MagnitudeAt(power),
        };

        var entries = new Dictionary<IEffectEntry, EffectAmountPreview>(ReferenceEqualityComparer.Instance);
        EffectEntries.ForEach(aura.Effects, entry => entries.TryAdd(entry, PreviewEntry(entry, context)));
        return entries;
    }

    /// <summary>A value that changes whenever anything a preview for entityId reads about it does -- its stat modifiers or its ability scores -- so a window showing one can tell when to work it out again.</summary>
    public int ChangeStampOf(int entityId)
    {
        var abilityScores = effectServices.AbilityScores;
        var abilityScoresVersion = abilityScores.Has(entityId) ? abilityScores.GetVersion(entityId) : 0u;
        return HashCode.Combine(effectServices.StatModifiers.GetEntityVersion(entityId), abilityScoresVersion);
    }

    private static EffectAmountPreview PreviewEntry(IEffectEntry entry, EffectContext context)
    {
        var preview = new EffectAmountPreview();
        entry.PreviewAmounts(context with { AmountPreview = preview }, preview);
        return preview;
    }

    private static PreviewedValue SpellValue(int declared, int scaled, float multiplier) =>
        new(declared, scaled, multiplier == 1f ? [] : [ValueContribution.Multiplier(ValueContributionKind.SpellIntelligence, multiplier)]);
}
