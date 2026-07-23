using Game.Notifications;

namespace Presentation.UI.Notifications;

/// <summary>A single notification's content, independent of whether it's currently shown or queued unread.</summary>
public sealed class Notification(string text, NotificationCategory category, string? title = null)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; set; } = text;
    public NotificationCategory Category { get; } = category;

    /// <summary>Overrides the active popup's title bar text -- see NotificationCenter.ShowActive, which falls back to Category.ToString() when this is null.</summary>
    public string? Title { get; } = title;
}