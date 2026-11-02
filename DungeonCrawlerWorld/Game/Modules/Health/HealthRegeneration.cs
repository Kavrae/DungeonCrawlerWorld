using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>An entity's natural health regeneration rate, shared by the regen systems and the views that show it.</summary>
/// <remarks>
/// Constitution sets the base rate; HealthRegen, then the entity's own OutgoingHealing and
/// IncomingHealing (regen is a self-heal), scale it -- all per second, so a modifier means the same
/// whatever tier the entity is in and however often its stripe is visited. A regen tick heals this
/// rate times the seconds since its last visit, and HealthHeal does not scale it again.
/// </remarks>
public static class HealthRegeneration
{
    /// <summary>Flat HP/sec at Constitution total 1.</summary>
    private const float MinimumHealthRegenPerSecond = 2f;

    /// <summary>Flat HP/sec at Constitution total 300.</summary>
    private const float MaximumHealthRegenPerSecond = 6f;

    /// <summary>The health entityId's regeneration restores per second, or 0 when it has no Constitution score.</summary>
    public static float LandedPerSecond(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId)
    {
        if (!AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Constitution, out var constitution))
        {
            return 0f;
        }

        var basePerSecond = AbilityScoreMath.Lerp(constitution.Total, MinimumHealthRegenPerSecond, MaximumHealthRegenPerSecond);
        var regenPerSecond = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.HealthRegen, basePerSecond);
        return HealthHeal.ScaleByHealingModifiers(statModifiers, entityId, entityId, default, regenPerSecond);
    }
}
