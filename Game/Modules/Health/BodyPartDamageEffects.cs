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

/// <summary>Shared per-part damage application, extracted so every damage source that already knows which one part it's hitting (ComplexHealthDamage.Apply, BodyPartBurningSystem's own DoT tick) applies the exact same clamp-and-disable and event-publishing rules instead of re-implementing them.</summary>
public static class BodyPartDamageEffects
{
    /// <summary>Clamps denseIndex's CurrentHealth down by amount against its modifier-effective MaximumHealth, disabling the part (and resetting a fresh 10-second RegenLockoutFramesRemaining) the instant it lands at 0 -- re-armed on every hit that leaves it at 0, not only the first transition into 0.</summary>
    public static void ApplyToPart(MultiComponentPool<BodyPartComponent> bodyParts, int denseIndex, MultiComponentPool<StatModifierComponent>? statModifiers, int entityId, ushort amount)
    {
        bodyParts.UpdateByDenseIndex(denseIndex, (statModifiers, entityId, amount), static (ref BodyPartComponent part, (MultiComponentPool<StatModifierComponent>? StatModifiers, int EntityId, ushort Amount) state) =>
        {
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(state.StatModifiers, state.EntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            part.CurrentHealth = MathHelper.Clamp(part.CurrentHealth - state.Amount, 0f, effectiveMaximumHealth);

            if (part.CurrentHealth == 0)
            {
                part.IsDisabled = true;
                part.RegenLockoutFramesRemaining = (ushort)(10 * GameTiming.FramesPerSecond);
            }
        });
    }

    /// <summary>Unconditionally refreshes denseIndex's RegenLockoutFramesRemaining to a fresh 10 seconds, regardless of whether this hit actually landed the part at 0.</summary>
    /// <remarks>
    /// For an ongoing per-tick damage source (BodyPartBurningSystem) whose single tick often
    /// doesn't deal enough damage to zero out a small part (e.g. a 10 HP Foot against a
    /// lightly-stacked burn) -- without this, ApplyToPart's own 0-only lockout never engages at
    /// all, and the *only* thing excluding the part from regen is BodyPartSelection.
    /// PickLowestPercentage's separate "is currently burning" check, which stops applying the
    /// instant the fire's last stack ticks off -- giving zero cooldown after the fire genuinely
    /// goes out. Calling this every burn tick means the lockout is always freshly 10 seconds out
    /// from the *last* tick, so there's a real grace period once burning actually stops, not just
    /// while it's active. Not called by ComplexHealthDamage's own single discrete hits (melee,
    /// spells) -- a one-off hit that doesn't finish a part off shouldn't lock it out of regen for
    /// 10 seconds; only a sustained per-tick affliction should.
    /// </remarks>
    public static void ResetRegenLockout(MultiComponentPool<BodyPartComponent> bodyParts, int denseIndex) =>
        bodyParts.UpdateByDenseIndex(denseIndex, static (ref BodyPartComponent part) => part.RegenLockoutFramesRemaining = (ushort)(10 * GameTiming.FramesPerSecond));

    /// <summary>Publishes EntityDiedEvent (on a Vital part's own wasAlive-to-0 transition) and, for player-involved damage, EntityDamagedEvent with the entity's real summed totals -- the same post-clamp bookkeeping ComplexHealthDamage.Apply always did inline, now shared with BodyPartBurningSystem's own tick.</summary>
    public static void PublishDamageEvents(
        PackedComponentPool<SimpleHealthComponent> health,
        MultiComponentPool<BodyPartComponent> bodyParts,
        EventBus eventBus,
        int denseIndex,
        int entityId,
        ushort effectiveAmount,
        StatusEffectSource source,
        IPlayerQuery? playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent>? statModifiers,
        PackedComponentPool<DeadComponent>? deadEntities)
    {
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
