using Engine.ECS.Components.Stores;
using Game.Modules.Achievements.Components;

namespace Game.Modules.Achievements;

/// <summary>Provides query operations for retrieving achievement-related components.</summary>
/// <remarks>
/// MultiComponentPool has no built-in "does this entity's chain contain a match for field X"
/// accessor -- an entity can unlock several achievements, each its own
/// AchievementUnlockedComponent instance, so the pool only exposes a generic dense-chain walk
/// plus predicate-based helpers (TryGetFirst), deliberately blind to what AchievementId means.
/// This class owns the "match by AchievementId" predicate in one place instead of every caller
/// re-writing the same chain-walk + inline predicate.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class AchievementQueries
{
    /// <summary>Determines whether the specified entity has earned the specified achievement.</summary>
    /// <param name="unlockedAchievements">The pool of unlocked achievement components.</param>
    /// <param name="entityId">The ID of the entity for which to check achievement status.</param>
    /// <param name="achievementId">The ID of the achievement to check.</param>
    /// <returns>true if the entity has earned the achievement; otherwise, false.</returns>
    public static bool HasEarned(MultiComponentPool<AchievementUnlockedComponent> unlockedAchievements, int entityId, Guid achievementId) =>
        unlockedAchievements.TryGetFirst(entityId, achievementId, static (ref readonly AchievementUnlockedComponent unlocked, Guid id) => unlocked.AchievementId == id, out _);
}
