namespace Presentation.UI.ChromeBehaviors;

/// <summary>
/// Adds a single title button that toggles between minimize ("_") and restore ("O") --
/// minimize is only ever valid on a restored window and restore only ever valid on a
/// minimized one, so this is one button whose label tracks the window's current state,
/// rather than two independently-attached behaviors that would otherwise both be visible at
/// once regardless of which one actually applies. The label is kept in sync via
/// Window.DisplayModeChanged rather than updated only inside this button's own click handler,
/// since other code (e.g. a future "minimize all" action) can also toggle a window's
/// minimized state -- the button must reflect that too, not just clicks on itself.
/// </summary>
public sealed class WindowMinimizeRestoreBehavior : IWindowChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var button = new Button(window, new ButtonOptions());
        UpdateButtonLabel(window, button);

        button.Clicked += () => window.SetWindowDisplayMode(
            window.WindowDisplay == WindowDisplayMode.Minimized ? window.PreviousWindowDisplay : WindowDisplayMode.Minimized);

        void OnDisplayModeChanged(Window _) => UpdateButtonLabel(window, button);

        // WindowService pools and reuses Window instances across close/reopen cycles (see
        // NotificationCenter.OnActiveNotificationClosed for the same pattern), so this
        // subscription must detach itself on Closed -- otherwise it stays attached to the
        // pooled instance forever, accumulating one stale handler (pinning a discarded
        // button) per reuse cycle.
        void OnClosed(Window closedWindow)
        {
            closedWindow.DisplayModeChanged -= OnDisplayModeChanged;
            closedWindow.Closed -= OnClosed;
        }

        window.DisplayModeChanged += OnDisplayModeChanged;
        window.Closed += OnClosed;

        window.AddTitleButton(button);
    }

    private static void UpdateButtonLabel(Window window, Button button)
    {
        var isMinimized = window.WindowDisplay == WindowDisplayMode.Minimized;
        button.SetText(isMinimized ? "O" : "_");
    }
}
