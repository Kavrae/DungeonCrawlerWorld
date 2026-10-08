using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>Shared per-part damage application, extracted so every damage source that already knows which one part it's hitting (ComplexHealthDamage.Apply, BodyPartBurningSystem's own DoT tick) applies the exact same clamp-and-disable and event-publishing rules instead of re-implementing them.</summary>
public static class BodyPartDamageEffects
{
    /// <summary>How long a part stays out of passive regen once it is disabled, or once a sustained affliction last ticked on it.</summary>
    public const ushort RegenLockoutFrames = 10 * GameTiming.FramesPerSecond;

    /// <summary>Clamps the part's current health down by amount against its modifier-effective MaximumHealth, disabling the part (and locking it out of regen for a fresh 10 seconds from now) the instant it lands at 0 -- re-armed on every hit that leaves it at 0, not only the first transition into 0.</summary>
    /// <param name="now">The simulation frame this hit lands on -- the lockout is a deadline measured from it (see BodyPartStateComponent).</param>
    public static void ApplyToPart(EntityBodyParts bodyParts, int entityId, int partId, MultiComponentPool<StatModifierComponent> statModifiers, float amount, long now)
    {
        ArgumentNullException.ThrowIfNull(bodyParts);

        if (!bodyParts.TryGet(entityId, partId, out var part))
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
        bodyParts.Damage(entityId, partId, amount, effectiveMaximumHealth, now, RegenLockoutFrames);
    }

    /// <summary>Unconditionally pushes the part's regen lockout out to a fresh 10 seconds from now, regardless of whether this hit actually landed the part at 0.</summary>
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
    public static void ResetRegenLockout(EntityBodyParts bodyParts, int entityId, int partId, long now) =>
        bodyParts.LockOutOfRegen(entityId, partId, now, RegenLockoutFrames);

    /// <summary>Publishes EntityDiedEvent (on a Vital part's own wasAlive-to-0 transition) and, for player-involved damage, EntityDamagedEvent with the entity's real summed totals -- the same post-clamp bookkeeping ComplexHealthDamage.Apply always did inline, now shared with BodyPartBurningSystem's own tick.</summary>
    public static void PublishDamageEvents(
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        int entityId,
        int partId,
        float effectiveAmount,
        ActionSource source,
        IPlayerQuery playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<DeadComponent> deadEntities)
    {
        bodyParts.TryGet(entityId, partId, out var updatedPart);

        if (updatedPart.IsVital && updatedPart.CurrentHealth == 0 && !deadEntities.Has(entityId) && entityId != playerQuery.PlayerEntityId)
        {
            eventBus.Publish(new EntityDiedEvent(entityId, source));
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId
            || source.IsEntity(playerQuery.PlayerEntityKey);
        if (!playerInvolved)
        {
            return;
        }

        HealthQueries.TryGetTotals(health, bodyParts, entityId, out var totalCurrent, out var totalMaximum);
        var effectiveMaximumHealthForEvent = MathUtility.ClampUShort(StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, totalMaximum), 0, ushort.MaxValue);
        eventBus.Publish(new EntityDamagedEvent(entityId, effectiveAmount, source, MathUtility.ClampUShort(totalCurrent, 0, ushort.MaxValue), effectiveMaximumHealthForEvent, damageType));
    }

    /// <summary>
    /// BodyPartTargetMode.All counterpart to PublishDamageEvents -- that method is shaped around
    /// one already-known part; this one scans every part entityId owns once a whole-entity
    /// hit has already been applied to all of them, so a fireball fires at most one
    /// EntityDiedEvent (any Vital part landed at 0) and exactly one aggregate EntityDamagedEvent
    /// for the whole hit, not one of each per part.
    /// </summary>
    public static void PublishAggregateDamageEvents(
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        EventBus eventBus,
        int entityId,
        float effectiveAmount,
        ActionSource source,
        IPlayerQuery playerQuery,
        string damageType,
        MultiComponentPool<StatModifierComponent> statModifiers,
        PackedComponentPool<DeadComponent> deadEntities)
    {
        var anyVitalPartAtZero = false;
        foreach (var part in bodyParts.Parts(entityId))
        {
            if (part.IsVital && part.CurrentHealth == 0)
            {
                anyVitalPartAtZero = true;
                break;
            }
        }

        if (anyVitalPartAtZero && !deadEntities.Has(entityId) && entityId != playerQuery.PlayerEntityId)
        {
            eventBus.Publish(new EntityDiedEvent(entityId, source));
        }

        var playerInvolved = entityId == playerQuery.PlayerEntityId
            || source.IsEntity(playerQuery.PlayerEntityKey);
        if (!playerInvolved)
        {
            return;
        }

        HealthQueries.TryGetTotals(health, bodyParts, entityId, out var totalCurrent, out var totalMaximum);
        var effectiveMaximumHealthForEvent = MathUtility.ClampUShort(StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, totalMaximum), 0, ushort.MaxValue);
        eventBus.Publish(new EntityDamagedEvent(entityId, effectiveAmount, source, MathUtility.ClampUShort(totalCurrent, 0, ushort.MaxValue), effectiveMaximumHealthForEvent, damageType));
    }
}
