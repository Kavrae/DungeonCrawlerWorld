using UIEngine.UI.Content;

namespace Presentation.UI;

/// <summary>Says "Denied while Paused" at the cursor whenever ActionTargetingController refuses an activation because the game is paused.</summary>
/// <remarks>Arming is allowed while paused, so the text answers only a confirm, a toggle switched or something put down.</remarks>
public sealed class PausedActivationCursorText
{
    public PausedActivationCursorText(CursorTextContent cursorText, ActionTargetingController actionTargeting) =>
        actionTargeting.ActivationDeniedWhilePaused += () => cursorText.Show(ActionTargetingController.DeniedWhilePausedText);
}
