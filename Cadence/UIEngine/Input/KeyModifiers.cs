using Microsoft.Xna.Framework.Input;

namespace UIEngine.Input;

/// <summary>The modifier keys held with a hotkey; either the left or the right key of each counts.</summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Control = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
}

public static class KeyModifiersExtensions
{
    /// <summary>The modifiers held down in keyboardState.</summary>
    public static KeyModifiers HeldModifiers(this KeyboardState keyboardState)
    {
        var modifiers = KeyModifiers.None;
        if (keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt))
        {
            modifiers |= KeyModifiers.Alt;
        }

        return modifiers;
    }
}
