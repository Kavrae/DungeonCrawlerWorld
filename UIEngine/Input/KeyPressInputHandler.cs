using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>A key that does one thing when pressed, wherever focus is -- a window's toggle hotkey, a mode switch.</summary>
/// <remarks>
/// Edge-triggered and not gated on menu mode, like Tab and Escape. Ignored while a TextBox has focus unless
/// firesWhileTyping, so a printable key stays typed text.
/// </remarks>
public sealed class KeyPressInputHandler(Keys key, Action onPressed, bool firesWhileTyping = false) : IUiInputHandler
{
    public void HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused)
    {
        if ((isTextBoxFocused && !firesWhileTyping) || !Element.WasKeyPressed(current, previous, key))
        {
            return;
        }

        onPressed();
    }
}
