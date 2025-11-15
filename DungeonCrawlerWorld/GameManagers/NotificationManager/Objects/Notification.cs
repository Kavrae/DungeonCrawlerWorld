using System;

namespace DungeonCrawlerWorld.GameManagers.NotificationManager
{
    /// <summary>
    /// Represents a notification within the game, including its text content, type, and active status.
    /// </summary>
    public class Notification
    {
        public Guid Id { get; }

        /// <summary>
        /// The text content of the notification.
        /// </summary>
        /// <todo>Split into header and content text</todo>
        public string Text { get; set; }

        /// <summary>
        /// When set to true, the notification appears as a popup on the user interface.
        /// When set to false, the notification is added to the appropriate notification summary list.
        /// </summary>
        public bool IsActive { get; set; }

        public NotificationType NotificationType { get; set; }

        public Notification(string text, NotificationType notificationType)
        {
            Id = Guid.NewGuid();
            Text = text;
            IsActive = false;
            NotificationType = notificationType;
        }
    }
}