using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Mana;

/// <summary>An entity's natural mana regeneration rate, shared by ManaRegenSystem and the views that show it.</summary>
public static class ManaRegeneration
{
    /// <summary>Mana/sec at Intelligence total 1.</summary>
    private const float MinimumManaRegenPerSecond = 0.1f;

    /// <summary>Mana/sec at Intelligence total 300.</summary>
    private const float MaximumManaRegenPerSecond = 0.3f;

    /// <summary>The mana entityId regenerates per second before ManaRegen modifiers, if it has an Intelligence score.</summary>
    public static bool TryGetBasePerSecond(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId, out float basePerSecond)
    {
        if (!AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Intelligence, out var intelligence))
        {
            basePerSecond = 0f;
            return false;
        }

        basePerSecond = AbilityScoreMath.Lerp(intelligence.Total, MinimumManaRegenPerSecond, MaximumManaRegenPerSecond);
        return true;
    }

    /// <summary>The mana entityId regenerates per second, or 0 when it has no Intelligence score.</summary>
    public static float EffectivePerSecond(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId) =>
        TryGetBasePerSecond(abilityScores, entityId, out var basePerSecond)
            ? StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.ManaRegen, basePerSecond)
            : 0f;
}
