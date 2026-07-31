namespace Game.Notifications;

/// <summary>
/// The extra fields an achievement notification shows beyond the base Text/Title -- lives in
/// Game (like NotificationCategory) so a Game-layer achievement system can build one without
/// referencing Presentation. LootboxLabel is null when the achievement carries no lootbox
/// (an achievement always comes with 0 or 1, never more).
/// </summary>
public sealed record AchievementNotificationDetails(string RequirementText, string? LootboxLabel, string RewardText);
