namespace Game.Notifications;

/// <summary>
/// Lives in Game, not Presentation, specifically so a Game-layer system can publish a
/// NotificationRequestedEvent without referencing Presentation (Game has no reference to
/// Presentation, by design) -- Presentation is explicitly allowed to reference Game, so
/// NotificationCenter uses this directly instead of maintaining a parallel Presentation-side
/// enum with a translation step.
/// </summary>
public enum NotificationCategory
{
    /// <summary>A system event or update. Cannot be minimized, and pauses the game while active.</summary>
    System,

    /// <summary>A quest or objective. Can be minimized and does not pause the game.</summary>
    Quest,

    /// <summary>An achievement earned by the player. Can be minimized and does not pause the game; starts minimized (see AchievementModule) rather than popping up immediately.</summary>
    Achievement,
}