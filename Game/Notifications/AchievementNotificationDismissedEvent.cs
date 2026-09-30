namespace Game.Notifications;

/// <summary>The player closed an achievement's notification for good -- its Close button, "Close" or "Close All", never minimizing.</summary>
/// <remarks>Immediate, not buffered: the simulation that drains buffered events doesn't run while paused or in menu mode, and what it triggers (claiming the achievement's loot box) must land before the player can look for it.</remarks>
public readonly record struct AchievementNotificationDismissedEvent(Guid AchievementId);
