using Engine.ECS.Properties;

namespace Game.Modules.Achievements.Components;

/// <summary>The record of an achievement being unlocked.</summary>
/// <param name="achievementId">The unique identifier for the achievement.</param>
/// <param name="earnedAtUtcTicks">The UTC ticks when the achievement was earned.</param>
/// <cleanupVersion>1</cleanupVersion>
[DescribedComponent]
public readonly partial struct AchievementUnlockedComponent(Guid achievementId, long earnedAtUtcTicks)
{
    [PropertyMeaning(GamePropertyMeanings.AchievementId)]
    [ReadOnlyProperty]
    public Guid AchievementId { get; init; } = achievementId;
    [PropertyMeaning(GamePropertyMeanings.UtcTicks)]
    public long EarnedAtUtcTicks { get; init; } = earnedAtUtcTicks;
}
