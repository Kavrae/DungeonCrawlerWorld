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
public sealed class MinimizeRestoreBehavior : IChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var button = new Button(window, new ButtonOptions());
        UpdateButtonLabel(window, button);

        button.Clicked += () => window.SetDisplayMode(
            window.DisplayMode == ElementDisplayMode.Minimized
                ? window.PreviousDisplay
                : ElementDisplayMode.Minimized);

        // No manual detach needed on close -- ElementPoolService.CloseElement clears every
        // event on a pooled Element (DisplayModeChanged included) before it goes back into its
        // pool, so this subscription can't outlive the reuse cycle.
        window.DisplayModeChanged += _ => UpdateButtonLabel(window, button);

        window.AddTitleButton(button);
    }

    private static void UpdateButtonLabel(Window window, Button button)
    {
        var isMinimized = window.DisplayMode == ElementDisplayMode.Minimized;
        button.SetText(
            isMinimized
                ? "O"
                : "_");
    }
}