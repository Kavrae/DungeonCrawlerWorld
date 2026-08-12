namespace Game.Modules.Achievements;

/// <summary>
/// Static content plus trigger wiring for one achievement -- mirrors ActionDefinition's role
/// as a small self-contained content record, except an achievement also owns behavior
/// (RegisterTrigger) since "when is this earned" varies per achievement rather than being data
/// a shared system can interpret uniformly.
/// </summary>
public interface IAchievementDefinition
{
    Guid Id { get; }

    string Name { get; }

    /// <summary>The specific requirement that was fulfilled -- shown in the notification alongside Name/Description.</summary>
    string RequirementText { get; }

    string Description { get; }

    /// <summary>Null when this achievement carries no lootbox -- an achievement always comes with 0 or 1, never more.</summary>
    LootboxReward? Lootbox { get; }

    string RewardText { get; }

    /// <summary>
    /// Subscribes whatever EventBus handler(s) this achievement needs to detect its own
    /// condition. Implementations should use AchievementTriggerContext.SubscribeUntilUnlockedForPlayer
    /// rather than subscribing directly, so the achievement stops listening the moment it's
    /// earned instead of staying subscribed for the rest of the session.
    /// </summary>
    void RegisterTrigger(AchievementTriggerContext context);
}
