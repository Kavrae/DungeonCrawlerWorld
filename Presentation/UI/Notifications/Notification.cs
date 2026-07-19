using Game.Notifications;

namespace Presentation.UI.Notifications;

/// <summary>A single notification's content, independent of whether it's currently shown or queued unread.</summary>
public sealed class Notification(string text, NotificationCategory category)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; set; } = text;
    public NotificationCategory Category { get; } = category;
}
