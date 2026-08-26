using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;

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

        bodyParts.UpdateByDenseIndex(denseIndex, (statModifiers, entityId, effectiveAmount), static (ref BodyPartComponent part, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, ushort Amount) state) =>
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth - state.Amount, 0f, effectiveMaximumHealth);

            if (part.CurrentHealth == 0)
            {
                part.IsDisabled = true;
                part.RegenLockoutFramesRemaining = (ushort)(10 * GameTiming.FramesPerSecond);
            }
        });

        ref readonly var updatedPart = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);

        if (updatedPart.IsVital && updatedPart.CurrentHealth == 0 && deadEntities?.Has(entityId) != true && entityId != playerQuery?.PlayerEntityId)
        {
            eventBus.Publish(new EntityDiedEvent(entityId, source));
        }

        if (playerQuery is null)
        {
            return;
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId
            || (source.Kind == StatusEffectSourceKind.Entity && source.EntityId == playerQuery.PlayerEntityId);
        if (!playerInvolved)
        {
            return;
        }

        HealthQueries.TryGetTotals(health, bodyParts, entityId, out var totalCurrent, out var totalMaximum);
        var effectiveMaximumHealthForEvent = MathUtility.ClampUShort(StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, totalMaximum), 0, ushort.MaxValue);
        eventBus.Publish(new EntityDamagedEvent(entityId, effectiveAmount, source, MathUtility.ClampUShort(totalCurrent, 0, ushort.MaxValue), effectiveMaximumHealthForEvent, damageType));
    }
}
