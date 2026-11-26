using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Where the menu sidebar sits -- see HudChrome's own doc comment for why these are plain mutable fields rather than readonly. Its size is HotbarContent.MenuSidebarSize.</summary>
public static class MenuSidebarChrome
{
    /// <summary>Beneath the Notification folder, with enough clearance that the folder never overlaps the sidebar even fully expanded (NotificationChrome.FolderMaximumSize).</summary>
    public static Vector2 NotificationClearanceGap = new(0, 20);

    public static Vector2 Position = HudChrome.Margin + new Vector2(0, NotificationChrome.FolderMaximumSize.Y) + NotificationClearanceGap;
}
