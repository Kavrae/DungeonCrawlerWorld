using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores.Components;

namespace Game.Modules.AbilityScores;

/// <summary>Provides query operations for retrieving ability score components.</summary>
/// <remarks>
/// MultiComponentPool has no built-in "get the instance matching field X" accessor -- an entity
/// owns one AbilityScoreComponent per AbilityScoreType, so the pool only exposes a generic
/// dense-chain walk plus predicate-based helpers (TryGetFirst), deliberately blind to what
/// AbilityScoreType means. This class owns the "match by AbilityScoreType" predicate in one
/// place instead of every caller re-writing the same chain-walk + inline predicate.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class AbilityScoreQueries
{
    /// <summary>Tries to get the ability score component for the specified entity and type.</summary>
    /// <param name="abilityScores">The pool of ability score components.</param>
    /// <param name="entityId">The ID of the entity for which to retrieve the component.</param>
    /// <param name="type">The type of the ability score.</param>
    /// <param name="component">When this method returns true, contains the retrieved component; otherwise, contains the default value.</param>
    /// <returns>true if the component was found; otherwise, false.</returns>
    public static bool TryGetComponent(MultiComponentPool<AbilityScoreComponent> abilityScores, int entityId, AbilityScoreType type, out AbilityScoreComponent component) =>
        abilityScores.TryGetFirst(entityId, type, static (ref readonly AbilityScoreComponent candidate, AbilityScoreType t) => candidate.Type == t, out component);
}
