using Engine.ECS.Components.Stores;
using Game.Modules.Achievements.Components;

namespace Game.Modules.Achievements;

/// <summary>Shared read helper over the AchievementUnlockedComponent pool -- same shape as StatusEffectQueries.HasStack.</summary>
public static class AchievementQueries
{
    public static bool HasEarned(MultiComponentPool<AchievementUnlockedComponent> unlockedAchievements, int entityId, Guid achievementId)
    {
        for (var denseIndex = unlockedAchievements.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = unlockedAchievements.GetNextDenseIndex(denseIndex))
        {
            if (unlockedAchievements.GetReadonlyByDenseIndex(denseIndex).AchievementId == achievementId)
            {
                return true;
            }
        }

        return false;
    }
}
