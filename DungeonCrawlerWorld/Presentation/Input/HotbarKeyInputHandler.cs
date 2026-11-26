using Microsoft.Xna.Framework.Input;
using Presentation.UI;
using UIEngine.Input;

namespace Presentation.Input;

/// <summary>Every hotbar slot's key, the bar's and the menu sidebar's alike: each presses its slot, as a click on it does, wherever focus is -- and, every frame, writes an activation such a press buffered.</summary>
/// <remarks>
/// Edge-triggered and ignored while a TextBox has focus, so a printable key stays typed text. Whether a press does
/// anything while the game is paused is what the slot holds, not where it sits (ActionTargetingController.HandleHotkeySlotPress).
/// A press made while its action is cooling down is buffered; it is written here once ready
/// (ActionTargetingController.FlushBufferedActivation), not only while the map has focus.
/// </remarks>
public sealed class HotbarKeyInputHandler(ActionTargetingController actionTargeting) : IUiInputHandler
{
    public void HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused)
    {
        if (!isTextBoxFocused)
        {
            actionTargeting.HandleHotbarHotkeys(current, previous);
        }

        actionTargeting.FlushBufferedActivation();
    }
}
