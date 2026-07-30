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
            var targetEntityId = mapQuery.GetEntityIdAt(tile);
            if (targetEntityId == -1)
            {
                continue;
            }

            HealthDamage.Apply(health, eventBus, targetEntityId, instance.DamageAmount, StatusEffectSource.FromEntity(sourceEntityId), playerQuery, ability.Name);
        }
    }
}
