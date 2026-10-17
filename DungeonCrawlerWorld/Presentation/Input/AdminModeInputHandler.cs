using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>F12 flips AdminMode.IsOn -- Debug builds only, compiled out in Release so the hotkey doesn't exist there; it fires even while a TextBox has focus.</summary>
public sealed class AdminModeInputHandler : IUiInputHandler
{
    public void HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused)
    {
#if DEBUG
        if (Element.WasKeyPressed(current, previous, Keys.F12))
        {
            AdminMode.IsOn = !AdminMode.IsOn;
        }
#endif
    }
}
