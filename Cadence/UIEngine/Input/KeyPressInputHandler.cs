using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace UIEngine.Input;

/// <summary>A key that does one thing when pressed, wherever focus is -- a window's toggle hotkey, a mode switch.</summary>
/// <remarks>
/// Edge-triggered and not gated on menu mode, like Tab and Escape. Ignored while a TextBox has focus unless
/// firesWhileTyping, so a printable key stays typed text. With no modifiers given it fires whatever modifiers are held;
/// with modifiers given (None included) it fires only when exactly those are held, so F5 and Ctrl+F5 can be two hotkeys.
/// </remarks>
public sealed class KeyPressInputHandler(Keys key, Action onPressed, bool firesWhileTyping = false, KeyModifiers? modifiers = null) : IUiInputHandler
{
    public void HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused)
    {
        if ((isTextBoxFocused && !firesWhileTyping) || !Element.WasKeyPressed(current, previous, key))
        {
            return;
        }

        if (modifiers is { } requiredModifiers && current.HeldModifiers() != requiredModifiers)
        {
            return;
        }

        onPressed();
    }
}
