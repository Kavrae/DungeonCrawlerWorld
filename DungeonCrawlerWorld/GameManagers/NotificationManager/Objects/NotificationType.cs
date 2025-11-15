namespace DungeonCrawlerWorld.GameManagers.NotificationManager
{
    /// <summary>
    /// Defines the types of notifications that can be generated.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// A notification related to system events or updates.
        /// </summary>
        /// <todo>System notifications cannot be minimized and pause the game</todo>
        System,
        /// <summary>
        /// A notification related to quest or objectives.
        /// Can be minimized and do not automatically pause the game.
        /// </summary>
        Quest
    }
}