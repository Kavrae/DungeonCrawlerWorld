using Game.Modules.Actions;
using Game.Views;
using Game.World;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>Switches the player's targeting mode between Target and Ground, showing the new mode at the cursor.</summary>
/// <remarks>
/// Refused while the player is winding up: the windup already holds the selection it was confirmed
/// with, so a switch then would change nothing it does. The refusal is shown at the cursor rather than
/// ignored, so the key is never silently dead.
/// </remarks>
public sealed class TargetingModeSwitch(MapViewState mapViewState, ActionStateView actionStateView, IPlayerQuery playerQuery, CursorTextContent cursorText)
{
    public const string RefusedDuringWindupText = "Can't change targeting during a windup";

    public void Switch()
    {
        if (actionStateView.TryGetPendingWindup(playerQuery.PlayerEntityId, out _))
        {
            cursorText.Show(RefusedDuringWindupText);
            return;
        }

        mapViewState.TargetingMode = mapViewState.TargetingMode == TargetingMode.Target ? TargetingMode.Ground : TargetingMode.Target;
        cursorText.Show(TextFor(mapViewState.TargetingMode));
    }

    public static string TextFor(TargetingMode mode) => mode == TargetingMode.Target ? "Targeting: Target" : "Targeting: Ground";
}
