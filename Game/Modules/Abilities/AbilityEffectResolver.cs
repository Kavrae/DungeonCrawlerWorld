using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Abilities;

/// <summary>
/// Applies an ability's effect to whatever occupies each of its resolved target tiles.
/// DamageAmount is first scaled by the *caster's* own active OutgoingDamage modifiers (consume),
/// then StatModifierGrants and StatusEffects are granted to every resolved target regardless of
/// whether that target actually has a HealthComponent/took damage (grant) -- see
/// StatModifierGrant's own doc comment for why the two aren't gated on each other. StatusEffects
/// grants go through the shared StatusEffectAuraApplierRegistry (see
/// Game.Modules.StatusEffects.IStatusEffectAuraApplier) the same registry Burning/Poison/
/// Paralysis already register into for aura-granted stacks -- this is simply a second, direct
/// grant path into the same plugin lookup, not a separate system. Applying a status effect never
/// depends on the target having a HealthComponent (see ParalysisModule for the concrete proof:
/// it doesn't gate on HealthComponent at all), so "immortal but affectable" targets work here
/// for free.
///
/// Damage is only ever dealt when instance.DamageAmount is greater than 0 -- a flat additive
/// OutgoingDamage modifier (e.g. a permanent +2) must not turn a genuinely non-damaging utility
/// ability (DamageAmount 0, e.g. a self-buff whose only real effect is a StatModifierGrant) into
/// one that deals a phantom hit; a modifier can only scale damage that already exists, never
/// create it from nothing.
/// </summary>
public static class AbilityEffectResolver
{
    public static void Apply(
        AbilityDefinition ability,
        AbilityInstanceComponent instance,
        int sourceEntityId,
        IReadOnlyList<Vector3Int> targetTiles,
        IMapQuery mapQuery,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        StatusEffectAuraApplierRegistry statusEffectAppliers,
        ComponentManager componentManager,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        eventBus.Publish(new AbilityActivatedEvent(sourceEntityId, ability.Id));

        var dealsDamage = instance.DamageAmount > 0;
        var effectiveDamage = dealsDamage
            ? (short)StatModifierMath.GetEffectiveValue(statModifiers, sourceEntityId, StatModifierTarget.OutgoingDamage, instance.DamageAmount)
            : (short)0;

        foreach (var tile in targetTiles)
        {
            var blockingEntityId = mapQuery.GetEntityIdAt(tile);
            if (blockingEntityId != -1)
            {
                if (dealsDamage)
                {
                    HealthDamage.Apply(health, eventBus, blockingEntityId, effectiveDamage, StatusEffectSource.FromEntity(sourceEntityId), playerQuery, ability.Name, statModifiers);
                }
                TryApplyHeal(ability, health, blockingEntityId, statModifiers);
                GrantStatModifiers(statModifiers, ability, blockingEntityId, sourceEntityId);
                GrantStatusEffects(statusEffectAppliers, componentManager, eventBus, ability, blockingEntityId, sourceEntityId, deadEntities);
            }

            // Tiny/Phasing entities never occupy the Blocking slot GetEntityIdAt just checked
            // (see World.IsBlocking), and any number of them can share a tile -- so hitting
            // "everyone standing here" means also applying to every non-Blocking entity the
            // position-keyed index reports, not just the one Blocking occupant.
            foreach (var nonBlockingEntityId in mapQuery.GetNonBlockingEntityIdsAt(tile))
            {
                if (dealsDamage)
                {
                    HealthDamage.Apply(health, eventBus, nonBlockingEntityId, effectiveDamage, StatusEffectSource.FromEntity(sourceEntityId), playerQuery, ability.Name, statModifiers);
                }
                TryApplyHeal(ability, health, nonBlockingEntityId, statModifiers);
                GrantStatModifiers(statModifiers, ability, nonBlockingEntityId, sourceEntityId);
                GrantStatusEffects(statusEffectAppliers, componentManager, eventBus, ability, nonBlockingEntityId, sourceEntityId, deadEntities);
            }
        }
    }

    /// <summary>
    /// Mirrors ConsumableActivationSystem.HealTarget: HealFraction is computed per target off the
    /// target's own effective MaximumHealth (not the caster's), so a splash-shaped heal hitting
    /// entities with different max HP heals each by its own fraction. No-op for a target with no
    /// HealthComponent (e.g. an "immortal" entity) -- see HealthHeal.Apply's own doc comment.
    /// </summary>
    private static void TryApplyHeal(AbilityDefinition ability, PackedComponentPool<HealthComponent> health, int targetEntityId, MultiComponentPool<StatModifierComponent>? statModifiers)
    {
        if (ability.Effect.HealFraction <= 0 || !health.TryGetReadonly(targetEntityId, out var targetHealth))
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, targetEntityId, StatModifierTarget.MaximumHealth, targetHealth.MaximumHealth);
        HealthHeal.Apply(health, targetEntityId, (short)(ability.Effect.HealFraction * effectiveMaximumHealth), statModifiers);
    }

    /// <summary>No-op when statModifiers is null (StatModifiersModule not registered in this build) -- same graceful-optional treatment as the damage/regen consume side.</summary>
    private static void GrantStatModifiers(MultiComponentPool<StatModifierComponent>? statModifiers, AbilityDefinition ability, int targetEntityId, int sourceEntityId)
    {
        if (statModifiers is null)
        {
            return;
        }

        foreach (var grant in ability.Effect.StatModifierGrants)
        {
            statModifiers.Add(targetEntityId, new StatModifierComponent(
                grant.Target, grant.Operation, grant.Polarity, grant.CanModify, grant.Magnitude, grant.DurationFrames, StatusEffectSource.FromEntity(sourceEntityId)));
        }
    }

    /// <summary>
    /// Silently skips any StatusEffectType with no registered IStatusEffectAuraApplier -- not an
    /// error, the same "not yet supported" treatment StatusEffectAuraSystem.GrantStacks already
    /// gives an unregistered effect type. Also skips a corpse entirely -- a dead target doesn't
    /// receive newly-granted effects (see DeathSystem/DeadComponent); an effect already active on
    /// an entity when it dies keeps ticking until it naturally expires, untouched here.
    /// </summary>
    private static void GrantStatusEffects(StatusEffectAuraApplierRegistry statusEffectAppliers, ComponentManager componentManager, EventBus eventBus, AbilityDefinition ability, int targetEntityId, int sourceEntityId, PackedComponentPool<DeadComponent>? deadEntities)
    {
        if (deadEntities?.Has(targetEntityId) == true)
        {
            return;
        }

        foreach (var effectType in ability.Effect.StatusEffects)
        {
            if (!statusEffectAppliers.TryGet(effectType, out var applier))
            {
                continue;
            }

            var source = StatusEffectSource.FromEntity(sourceEntityId);
            applier.ApplyStack(componentManager, targetEntityId, source);
            eventBus.Publish(new StatusEffectAppliedEvent(targetEntityId, effectType, source));
        }
    }
}
