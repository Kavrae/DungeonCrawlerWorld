using System.Runtime.InteropServices;

namespace UIEngine.Input;

/// <summary>Reads the user's double-click speed from the operating system.</summary>
/// <remarks>
/// Windows only, through user32's <c>GetDoubleClickTime</c>: a system-wide setting needing no window, so it can be read
/// before one exists. Elsewhere, or if the call fails, it is the fallback the caller gives.
/// </remarks>
internal static class OperatingSystemDoubleClickTime
{
    public static float ReadSecondsOr(float fallbackSeconds)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallbackSeconds;
        }

        try
        {
            var milliseconds = GetDoubleClickTime();
            return milliseconds == 0 ? fallbackSeconds : milliseconds / 1000f;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return fallbackSeconds;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}
