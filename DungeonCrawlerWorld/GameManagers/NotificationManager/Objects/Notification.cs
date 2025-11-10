using System;

namespace DungeonCrawlerWorld.GameManagers.NotificationManager
{
    public class Notification
    {
        public Guid Id { get; }
        public string Text { get; set; }

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