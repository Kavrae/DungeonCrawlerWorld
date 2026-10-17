using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.AbilityScores;

/// <summary>Provides query operations for retrieving ability scores.</summary>
/// <remarks>
/// An entity's scores are one AbilityScoresComponent holding every AbilityScoreType (see its own
/// doc comment), so a read is a pool lookup plus an index. This class owns that pair in one place
/// instead of every caller repeating the "has the component, and was this score granted" check.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class AbilityScoreQueries
{
    /// <summary>Tries to get the base and total for the specified entity and ability score type.</summary>
    /// <param name="abilityScores">The pool of ability score components.</param>
    /// <param name="entityId">The ID of the entity for which to retrieve the score.</param>
    /// <param name="type">The type of the ability score.</param>
    /// <param name="component">When this method returns true, contains the retrieved score; otherwise, contains the default value.</param>
    /// <returns>true if the entity has that ability score; otherwise, false.</returns>
    public static bool TryGetComponent(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId, AbilityScoreType type, out AbilityScoreValue component)
    {
        var denseIndex = abilityScores.GetDenseIndex(entityId);
        if (denseIndex < 0)
        {
            component = default;
            return false;
        }

        return abilityScores.GetReadonlyByDenseIndex(denseIndex).TryGet(type, out component);
    }
}
