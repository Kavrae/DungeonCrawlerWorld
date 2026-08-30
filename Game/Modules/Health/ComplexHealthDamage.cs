using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to HealthDamage.Apply's Simple path -- damages one selected body part instead of a single shared pool (see ApplyToAllParts below for the "every part at once" counterpart).</summary>
/// <remarks>
/// Its only caller is HealthDamage.Apply, once it's confirmed entityId owns no
/// SimpleHealthComponent but does own at least one BodyPartComponent. Mirrors the Simple path's
/// IncomingDamage-then-clamp-against-effective-MaximumHealth modifier chain, scoped to the one
/// part selected (BodyPartSelection.PickRandom/PickByTypeWithFallback/PickLowestPercentage,
/// depending on targetMode/targetRule), and disables that part (plus a 10-second
/// RegenLockoutFramesRemaining) the instant it lands at 0. EntityDiedEvent only fires off a
/// Vital part reaching 0 -- a Complex entity's summed total can still read well above 0 the
/// instant its last Vital part hits 0. EntityDamagedEvent's Current/MaximumHealth are still the
/// entity's real summed total (HealthQueries.TryGetTotals), not the single hit part, so the
/// HUD-facing event reports the same thing it would for a Simple entity.
/// </remarks>
public static class ComplexHealthDamage
{
    public static void Apply(
        PackedComponentPool<SimpleHealthComponent> health,
        MultiComponentPool<BodyPartComponent> bodyParts,
        EventBus eventBus,
        int entityId,
        ushort amount,
        StatusEffectSource source,
        IPlayerQuery? playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent>? statModifiers,
        MathUtility mathUtility,
        PackedComponentPool<DeadComponent>? deadEntities,
        BodyPartTargetRule? targetRule = null,
        IReadOnlyList<Tag>? damageTags = null,
        BodyPartTargetMode targetMode = BodyPartTargetMode.SingleTarget)
    {
        var denseIndex = targetMode == BodyPartTargetMode.LowestPercentage
            ? BodyPartSelection.PickLowestPercentage(bodyParts, entityId, statModifiers)
            : targetRule is { } rule
                ? BodyPartSelection.PickByTypeWithFallback(bodyParts, entityId, rule, mathUtility)
                : BodyPartSelection.PickRandom(bodyParts, entityId, mathUtility);
        if (denseIndex == -1)
        {
            return;
        }

        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount, damageTags),
            0,
            ushort.MaxValue);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers, entityId, effectiveAmount);
        BodyPartDamageEffects.PublishDamageEvents(health, bodyParts, eventBus, denseIndex, entityId, effectiveAmount, source, playerQuery, damageType, statModifiers, deadEntities);
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
    public static void ApplyToAllParts(
        PackedComponentPool<SimpleHealthComponent> health,
        MultiComponentPool<BodyPartComponent> bodyParts,
        EventBus eventBus,
        int entityId,
        ushort amount,
        StatusEffectSource source,
        IPlayerQuery? playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent>? statModifiers,
        PackedComponentPool<DeadComponent>? deadEntities,
        IReadOnlyList<Tag>? damageTags = null)
    {
        var partCount = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            partCount++;
        }

        if (partCount == 0)
        {
            return;
        }

        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount, damageTags),
            0,
            ushort.MaxValue);
        var perPartAmount = (ushort)(effectiveAmount / partCount);

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers, entityId, perPartAmount);
        }

        BodyPartDamageEffects.PublishAggregateDamageEvents(health, bodyParts, eventBus, entityId, effectiveAmount, source, playerQuery, damageType, statModifiers, deadEntities);
    }
}
