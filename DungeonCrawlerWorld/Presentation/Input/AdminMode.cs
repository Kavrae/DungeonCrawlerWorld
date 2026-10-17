using UIEngine.UI.Chrome;

namespace Presentation.Input;

/// <summary>The game's F12 debug mode: Admin Mode's map menus and inspection, hidden ability scores, and window geometry in titles.</summary>
/// <remarks>Process-wide, like the hotkey that flips it; a test that turns it on turns it off again in a finally.</remarks>
public static class AdminMode
{
    /// <summary>Whether Admin Mode is on. Turning it on also shows window geometry in titles (WindowDebugChrome).</summary>
    public static bool IsOn
    {
        get => WindowDebugChrome.ShowsGeometryInTitles;
        set => WindowDebugChrome.ShowsGeometryInTitles = value;
    }
}
