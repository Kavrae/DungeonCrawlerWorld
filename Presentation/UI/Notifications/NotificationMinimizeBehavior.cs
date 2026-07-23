namespace Presentation.UI.Notifications;

/// <summary>
/// Notification popups show the same "_" minimize-look button as any other window, but
/// clicking it doesn't shrink the window in place the way WindowMinimizeRestoreBehavior
/// does elsewhere -- it dismisses the popup and returns the notification to its category's
/// unread queue (via the supplied callback) so it can be reopened later from the summary
/// bar. This is a concrete case of a caller substituting its own semantics for a standard
/// chrome action -- IWindowChromeBehavior already supports this (nothing requires the
/// built-in behaviors), this is just the first thing that actually does it.
/// </summary>
public sealed class NotificationMinimizeBehavior(Action onMinimize) : IWindowChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var button = new Button(window, new ButtonOptions { Text = "_" });

        button.Clicked += onMinimize;
        window.AddTitleButton(button);
    }
}