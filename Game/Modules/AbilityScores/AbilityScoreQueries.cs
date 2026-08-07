using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.AbilityScores;

/// <summary>Read-side lookups over MultiComponentPool&lt;AbilityScoreComponent&gt; -- same shape as InventoryQueries, walking a MultiComponentPool's dense per-entity chain.</summary>
public static class AbilityScoreQueries
{
    /// <summary>Single-type lookup among entityId's (up to 7) AbilityScoreComponent instances.</summary>
    public static bool TryGetComponent(MultiComponentPool<AbilityScoreComponent> abilityScores, int entityId, AbilityScoreType type, out AbilityScoreComponent component)
    {
        for (var denseIndex = abilityScores.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = abilityScores.GetNextDenseIndex(denseIndex))
        {
            var candidate = abilityScores.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.Type == type)
            {
                component = candidate;
                return true;
            }
        }

        component = default;
        return false;
    }
}
