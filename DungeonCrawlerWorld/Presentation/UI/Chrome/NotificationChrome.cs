using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for NotificationCenter's own summary Folder and active
/// notification popups -- see HudChrome's own doc comment for why these are plain mutable fields
/// rather than readonly. FolderPosition/FolderMaximumSize are the head of a cross-controller
/// layout chain: HealthWindowChrome.ButtonPosition sits directly beneath FolderMaximumSize's
/// bottom edge, and InventoryChrome.FolderPosition in turn sits beneath that.</summary>
public static class NotificationChrome
{
    public static Vector2 FolderPosition = HudChrome.Margin;

    /// <summary>
    /// The largest the Folder's WrapContent sizing may grow -- comfortably larger than the widest/tallest
    /// the category stack can ever be. HealthWindowChrome positions its own button beneath this one,
    /// derived from this ceiling rather than a second, silently-driftable duplicate of the same number.
    /// </summary>
    public static Vector2 FolderMaximumSize = new(200, 400);

    /// <summary>
    /// Deliberately its own constant, not HudChrome.EntrySize (65px wide -- sized for short
    /// hotbar/health-bar-style content elsewhere). Also drives the Folder's own width, both
    /// expanded (RecalculateWrapContentWindowSize fits its title/content to the widest child)
    /// and collapsed (Folder.RecalculateMinimizedWindowSize matches that same width instead of
    /// shrinking to just its icon). Width is 117 (78 * 1.5) to keep pace with TextWindow.
    /// ContentFont's own 8 -> 12 (50%) increase -- otherwise the widest label ("Achievement: 0")
    /// would overflow the tile at the larger font.
    /// </summary>
    public static Vector2 SummaryEntrySize = new(117, HudChrome.EntrySize.Y);

    public static Vector2 ActiveNotificationBasePosition = new(200, 200);
    public static Vector2 ActiveNotificationMaximumSize = new(640, 176);
    public static int ActiveNotificationStackOffset = 10;
}
