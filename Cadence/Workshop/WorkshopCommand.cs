using Microsoft.Xna.Framework.Input;
using UIEngine.Input;

namespace Workshop;

/// <summary>A shell command: a tile on the right of the tab strip, and a hotkey that does the same.</summary>
/// <param name="Title">The tile's text; the hotkey is shown after it.</param>
/// <param name="Hotkey">The key that runs it from anywhere in the IDE, typing included.</param>
/// <param name="Modifiers">The modifiers held with Hotkey, exactly.</param>
/// <param name="Run">What it does; it may report on the shell's status line.</param>
/// <param name="IsShown">Whether its tile is on the strip now, asked every frame; null shows it always. The hotkey works either way.</param>
public sealed record WorkshopCommand(string Title, Keys Hotkey, KeyModifiers Modifiers, Action<IWorkshopStatus> Run, Func<bool>? IsShown = null)
{
    /// <summary>Whether its tile is on the strip now.</summary>
    public bool ShowsTile => IsShown?.Invoke() ?? true;

    /// <summary>"Ctrl+F5"-style text for the hotkey.</summary>
    public string HotkeyText
    {
        get
        {
            var prefix = (Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "")
                + (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
                + (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : "");
            return prefix + Hotkey;
        }
    }
}
