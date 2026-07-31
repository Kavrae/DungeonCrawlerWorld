namespace Game.Modules.Achievements.Components;

/// <summary>
/// Marks that an entity has earned a specific achievement. MultiComponentPool-backed since an
/// entity earns many achievements over time (RaceComponent is the same shape, for the same
/// reason). The real guarantee against earning the same achievement twice is each achievement's
/// trigger unsubscribing itself once satisfied (see AchievementTriggerContext.SubscribeUntilUnlocked)
/// -- this component is a secondary record of what's been earned, consulted by AchievementQueries.
/// </summary>
public struct AchievementUnlockedComponent(Guid achievementId, long earnedAtUtcTicks)
{
    public Guid AchievementId { get; } = achievementId;
    public long EarnedAtUtcTicks { get; } = earnedAtUtcTicks;
}
