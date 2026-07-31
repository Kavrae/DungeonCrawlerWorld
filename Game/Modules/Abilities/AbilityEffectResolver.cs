using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Game.Modules.Abilities;

/// <summary>
/// Applies an ability's effect to whatever occupies each of its resolved target tiles.
/// Damage-only for now -- an ability's StatusEffects (AbilityEffect.StatusEffects) aren't
/// applied here yet, since arbitrary status effects still depend on the unbuilt Engine generic
/// status-effect system (see TODO.md).
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
        IPlayerQuery? playerQuery)
    {
        foreach (var tile in targetTiles)
        {
            var blockingEntityId = mapQuery.GetEntityIdAt(tile);
            if (blockingEntityId != -1)
            {
                HealthDamage.Apply(health, eventBus, blockingEntityId, instance.DamageAmount, StatusEffectSource.FromEntity(sourceEntityId), playerQuery, ability.Name);
            }

            // Tiny/Phasing entities never occupy the Blocking slot GetEntityIdAt just checked
            // (see World.IsBlocking), and any number of them can share a tile -- so hitting
            // "everyone standing here" means also applying to every non-Blocking entity the
            // position-keyed index reports, not just the one Blocking occupant.
            foreach (var nonBlockingEntityId in mapQuery.GetNonBlockingEntityIdsAt(tile))
            {
                HealthDamage.Apply(health, eventBus, nonBlockingEntityId, instance.DamageAmount, StatusEffectSource.FromEntity(sourceEntityId), playerQuery, ability.Name);
            }
        }
    }
}
