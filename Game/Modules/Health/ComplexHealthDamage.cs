using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Complex-health counterpart to HealthDamage.Apply's Simple path -- damages one randomly selected body part instead of a single shared pool.</summary>
/// <remarks>
/// Its only caller is HealthDamage.Apply, once it's confirmed entityId owns no
/// SimpleHealthComponent but does own at least one BodyPartComponent. Mirrors the Simple path's
/// IncomingDamage-then-clamp-against-effective-MaximumHealth modifier chain, scoped to the one
/// part BodyPartSelection.PickRandom selects, and disables that part (plus a 10-second
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
        BodyPartTargetRule? targetRule = null)
    {
        var denseIndex = targetRule is { } rule
            ? BodyPartSelection.PickByTypeWithFallback(bodyParts, entityId, rule, mathUtility)
            : BodyPartSelection.PickRandom(bodyParts, entityId, mathUtility);
        if (denseIndex == -1)
        {
            return;
        }

        var effectiveAmount = MathUtility.ClampUShort(
            StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.IncomingDamage, amount),
            0,
            ushort.MaxValue);

        BodyPartDamageEffects.ApplyToPart(bodyParts, denseIndex, statModifiers, entityId, effectiveAmount);
        BodyPartDamageEffects.PublishDamageEvents(health, bodyParts, eventBus, denseIndex, entityId, effectiveAmount, source, playerQuery, damageType, statModifiers, deadEntities);
    }
}
