using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Tags;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to HealthDamage.Apply's Simple path -- damages one selected body part instead of a single shared pool (see ApplyToAllParts below for the "every part at once" counterpart).</summary>
/// <remarks>
/// Its only caller is HealthDamage.Apply, once it's confirmed entityId owns no
/// SimpleHealthComponent but does have a body plan. Mirrors the Simple path's
/// IncomingDamage-then-clamp-against-effective-MaximumHealth modifier chain, scoped to the one
/// part selected (BodyPartSelection.PickRandom/PickByTypeWithFallback/PickLowestPercentage,
/// depending on targetMode/targetRule), and disables that part (locking it out of passive regen
/// for 10 seconds from `now` -- see BodyPartStateComponent) the instant it lands at 0. EntityDiedEvent only fires off a
/// Vital part reaching 0 -- a Complex entity's summed total can still read well above 0 the
/// instant its last Vital part hits 0. EntityDamagedEvent's Current/MaximumHealth are still the
/// entity's real summed total (HealthQueries.TryGetTotals), not the single hit part, so the
/// HUD-facing event reports the same thing it would for a Simple entity.
/// </remarks>
public static class ComplexHealthDamage
{
    /// <returns>The damage dealt after the target's IncomingDamage modifiers, or 0 when entityId has no part to hit.</returns>
    public static ushort Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        int entityId,
        ushort amount,
        ActionSource source,
        IPlayerQuery playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent> statModifiers,
        MathUtility mathUtility,
        PackedComponentPool<DeadComponent> deadEntities,
        long now,
        BodyPartTargetRule? targetRule = null,
        GameplayTagSet damageTags = default,
        BodyPartTargetMode targetMode = BodyPartTargetMode.SingleTarget)
    {
        var partId = targetMode == BodyPartTargetMode.LowestPercentage
            ? BodyPartSelection.PickLowestPercentage(bodyParts, entityId, now, statModifiers)
            : targetRule is { } rule
                ? BodyPartSelection.PickByTypeWithFallback(bodyParts, entityId, rule, mathUtility)
                : BodyPartSelection.PickRandom(bodyParts, entityId, mathUtility);
        if (partId == -1)
        {
            return 0;
        }

        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount, damageTags),
            0,
            ushort.MaxValue);

        BodyPartDamageEffects.ApplyToPart(bodyParts, entityId, partId, statModifiers, effectiveAmount, now);
        BodyPartDamageEffects.PublishDamageEvents(health, bodyParts, eventBus, entityId, partId, effectiveAmount, source, playerQuery, damageType, statModifiers, deadEntities);
        return effectiveAmount;
    }

    /// <summary>
    /// BodyPartTargetMode.All counterpart to Apply's single-part logic -- IncomingDamage is
    /// computed exactly once against the full amount (never per part), then the resulting
    /// effective total is split evenly across however many parts entityId owns. Computing it once
    /// up front (rather than re-deriving it per part) matters for any additive modifier, not just
    /// a flat damage component: applying an additive IncomingDamage reduction to each of N parts
    /// independently would multiply its effect by N, which is exactly the "unfairly multiplied by
    /// body part count" bug this mode exists to avoid. Publishes one aggregate
    /// EntityDamagedEvent/EntityDiedEvent pair for the whole hit (BodyPartDamageEffects.
    /// PublishAggregateDamageEvents) rather than one per part, so a fireball reads as a single hit
    /// on the HUD/combat log, not N separate small ones.
    /// </summary>
    /// <returns>The whole hit's damage after the target's IncomingDamage modifiers, or 0 when entityId has no parts.</returns>
    public static ushort ApplyToAllParts(
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        int entityId,
        ushort amount,
        ActionSource source,
        IPlayerQuery playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<DeadComponent> deadEntities,
        long now,
        GameplayTagSet damageTags = default)
    {
        var partCount = bodyParts.Count(entityId);

        if (partCount == 0)
        {
            return 0;
        }

        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount, damageTags),
            0,
            ushort.MaxValue);
        var perPartAmount = (ushort)(effectiveAmount / partCount);

        for (var partId = 0; partId < partCount; partId++)
        {
            BodyPartDamageEffects.ApplyToPart(bodyParts, entityId, partId, statModifiers, perPartAmount, now);
        }

        BodyPartDamageEffects.PublishAggregateDamageEvents(health, bodyParts, eventBus, entityId, effectiveAmount, source, playerQuery, damageType, statModifiers, deadEntities);
        return effectiveAmount;
    }
}
