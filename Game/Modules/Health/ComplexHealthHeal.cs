using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.Health.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to a healing potion/scroll's DirectHeal, and to a regen tick's own periodic self-heal.</summary>
/// <remarks>
/// BodyPartTargetMode.All (ApplyToAllParts): the total heal amount is computed exactly once
/// against the entity's overall modifier-effective max health (HealthQueries.TryGetEffectiveMaximum,
/// summed across every part), via the same HealthHeal.ComputeAmount the Simple path uses, then
/// split evenly across however many parts the entity owns -- not scaled independently per part by
/// that part's own max, the way this used to work. This matters for FlatAmount and for any
/// additive OutgoingHealing/IncomingHealing modifier: applying either independently to N parts
/// would multiply its effect by N, exactly the "unfairly multiplied by body part count" bug this
/// mode exists to avoid (mirrors ComplexHealthDamage.ApplyToAllParts' identical reasoning).
/// SingleTarget/LowestPercentage (ApplyToSinglePart): the same total lands entirely on one
/// selected part -- random, a specific BodyPartType with fallback, or the most-damaged part
/// (BodyPartSelection.PickLowestPercentage, excluding a currently-burning part via
/// bodyPartBurningTimers the same way ComplexHealthRegenSystem's own tick always has). A
/// SingleTarget pick that needs MathUtility (random, or a fallback resolving to random) and finds
/// none wired in is a no-op, unlike HealthDamage.Apply's hard throw -- heal's default mode is All,
/// which never needs it, so mathUtility is optional here rather than a real construction bug.
/// Both apply-to-part paths clear IsDisabled the instant a part's CurrentHealth ticks back above
/// 0, and never check RegenLockoutFramesRemaining -- that lockout only ever gates passive regen,
/// never an active heal. Each publishes one aggregate EntityHealedEvent for the whole heal (via
/// HealthHeal.PublishHealEvent, reusing HealthQueries.TryGetTotals for the entity's real summed
/// current/max) rather than one per part.
/// </remarks>
public static class ComplexHealthHeal
{
    public static void ApplyToAllParts(
        MultiComponentPool<BodyPartComponent> bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        float flatAmount = 0f,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        int? sourceEntityId = null,
        IReadOnlyList<Tag>? activatorTags = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        string healType = "Heal")
    {
        var partCount = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            partCount++;
        }

        if (partCount == 0 || !HealthQueries.TryGetEffectiveMaximum(health, bodyParts, statModifiers, entityId, out var effectiveMaximumHealth))
        {
            return;
        }

        var totalAmount = HealthHeal.ComputeAmount(statModifiers, sourceEntityId, entityId, activatorTags, percentOfMaxHealth, flatAmount, effectiveMaximumHealth);
        var perPartAmount = totalAmount / partCount;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ApplyToPart(bodyParts, denseIndex, statModifiers, entityId, perPartAmount);
        }

        PublishAggregateHealEvent(bodyParts, health, eventBus, playerQuery, entityId, sourceEntityId, totalAmount, healType, statModifiers);
    }

    public static void ApplyToSinglePart(
        MultiComponentPool<BodyPartComponent> bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        int entityId,
        float percentOfMaxHealth,
        float flatAmount,
        MultiComponentPool<StatModifierComponent>? statModifiers,
        int? sourceEntityId,
        IReadOnlyList<Tag>? activatorTags,
        BodyPartTargetRule? targetRule,
        BodyPartTargetMode targetMode,
        MathUtility? mathUtility,
        MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers = null,
        EventBus? eventBus = null,
        IPlayerQuery? playerQuery = null,
        string healType = "Heal")
    {
        var denseIndex = ResolveDenseIndex(bodyParts, entityId, statModifiers, targetRule, targetMode, mathUtility, bodyPartBurningTimers);
        if (denseIndex == -1 || !HealthQueries.TryGetEffectiveMaximum(health, bodyParts, statModifiers, entityId, out var effectiveMaximumHealth))
        {
            return;
        }

        var amount = HealthHeal.ComputeAmount(statModifiers, sourceEntityId, entityId, activatorTags, percentOfMaxHealth, flatAmount, effectiveMaximumHealth);
        ApplyToPart(bodyParts, denseIndex, statModifiers, entityId, amount);

        PublishAggregateHealEvent(bodyParts, health, eventBus, playerQuery, entityId, sourceEntityId, amount, healType, statModifiers);
    }

    private static int ResolveDenseIndex(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MultiComponentPool<StatModifierComponent>? statModifiers, BodyPartTargetRule? targetRule, BodyPartTargetMode targetMode, MathUtility? mathUtility, MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers)
    {
        if (targetMode == BodyPartTargetMode.LowestPercentage)
        {
            return BodyPartSelection.PickLowestPercentage(bodyParts, entityId, statModifiers, bodyPartBurningTimers);
        }

        if (mathUtility is null)
        {
            return -1;
        }

        return targetRule is { } rule
            ? BodyPartSelection.PickByTypeWithFallback(bodyParts, entityId, rule, mathUtility)
            : BodyPartSelection.PickRandom(bodyParts, entityId, mathUtility);
    }

    private static void ApplyToPart(MultiComponentPool<BodyPartComponent> bodyParts, int denseIndex, MultiComponentPool<StatModifierComponent>? statModifiers, int entityId, float amount)
    {
        bodyParts.UpdateByDenseIndex(denseIndex, (amount, statModifiers, entityId), static (ref BodyPartComponent part, (float Amount, MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId) state) =>
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth + state.Amount, 0f, effectiveMaximumHealth);
            if (part.CurrentHealth > 0)
            {
                part.IsDisabled = false;
            }
        });
    }

    private static void PublishAggregateHealEvent(MultiComponentPool<BodyPartComponent> bodyParts, PackedComponentPool<SimpleHealthComponent> health, EventBus? eventBus, IPlayerQuery? playerQuery, int entityId, int? sourceEntityId, float amount, string healType, MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        if (eventBus is null || playerQuery is null)
        {
            return;
        }

        HealthQueries.TryGetTotals(health, bodyParts, entityId, out var totalCurrent, out var totalMaximum);
        var effectiveMaximumHealthForEvent = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, totalMaximum);
        HealthHeal.PublishHealEvent(eventBus, playerQuery, entityId, sourceEntityId, amount, healType, totalCurrent, effectiveMaximumHealthForEvent);
    }
}
