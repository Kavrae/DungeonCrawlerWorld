namespace Game.Modules.Achievements.Components;

/// <summary>The record of an achievement being unlocked.</summary>
/// <param name="achievementId">The unique identifier for the achievement.</param>
/// <param name="earnedAtUtcTicks">The UTC ticks when the achievement was earned.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct AchievementUnlockedComponent(Guid achievementId, long earnedAtUtcTicks)
{
    public Guid AchievementId { get; } = achievementId;
    public long EarnedAtUtcTicks { get; } = earnedAtUtcTicks;

    public override readonly string ToString() => $"AchievementId : {AchievementId}\nEarnedAtUtcTicks : {EarnedAtUtcTicks}";
}
