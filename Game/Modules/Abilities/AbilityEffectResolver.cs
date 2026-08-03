using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Abilities;

/// <summary>
/// Applies an ability's effect to whatever occupies each of its resolved target tiles. An
/// ability's StatusEffects (AbilityEffect.StatusEffects) still aren't applied here, since
/// arbitrary status effects depend on the unbuilt Engine generic status-effect system (see
/// TODO.md) -- but StatModifierGrants is, since that's the system this class now integrates
/// with in both directions: DamageAmount is first scaled by the *caster's* own active
/// OutgoingDamage modifiers (consume), then StatModifierGrants is granted to every resolved
/// target regardless of whether that target actually has a HealthComponent/took damage (grant)
/// -- see StatModifierGrant's own doc comment for why the two aren't gated on each other.
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
        MultiComponentPool<StatModifierComponent>? statModifiers = null)
    {
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
                GrantStatModifiers(statModifiers, ability, blockingEntityId, sourceEntityId);
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
                GrantStatModifiers(statModifiers, ability, nonBlockingEntityId, sourceEntityId);
            }
        }
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
}
