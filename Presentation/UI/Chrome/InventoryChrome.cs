using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for InventoryFolderController's own Folder and the two
/// windows it opens (Inventory, Ability Score) -- see HudChrome's own doc comment for why these
/// are plain mutable fields rather than readonly.</summary>
public static class InventoryChrome
{
    /// <summary>Between this Folder's own top edge and HealthWindowChrome's heart button sitting directly above it -- mirrors this field's own former shape/reasoning (it used to sit directly beneath the Notification folder; the heart button now occupies that slot instead, see HealthWindowChrome.ButtonPosition).</summary>
    public static Vector2 HealthButtonGap = new(0, 8);

    public static Vector2 FolderPosition = HealthWindowChrome.ButtonPosition + new Vector2(0, HealthWindowChrome.ButtonSize.Y) + HealthButtonGap;

    public static Vector2 TileSize = new(78, HudChrome.EntrySize.Y);

    /// <summary>Same reasoning as NotificationChrome.FolderMaximumSize -- a root WrapContent Folder's own MaximumSize is otherwise left at Vector2.Zero. Twice TileSize.Y tall, plus a little breathing room, since the folder now stacks two tiles (Inventory, Stats) rather than one. Its own field despite the identical bare name NotificationChrome.FolderMaximumSize once shared -- namespacing by class removes the confusing collision without falsely merging two genuinely different folders' values.</summary>
    public static Vector2 FolderMaximumSize = new(200, 180);

    /// <summary>Same value as HealthWindowChrome.WindowPosition today, but kept as its own independent field -- two separately-owned windows that happen to coincide, not one true duplicate.</summary>
    public static Vector2 WindowPosition = new(300, 150);

    /// <summary>Fixed width cap for the Ability Score hover popup; height auto-grows with content -- see Tooltip.</summary>
    public static Vector2 AbilityScoreHoverPopupMaximumSize = new(220, 10000f);

    /// <summary>Fixed width cap for the Inventory item hover popup; height auto-grows with content -- see Tooltip.</summary>
    public static Vector2 InventoryHoverPopupMaximumSize = new(220, 10000f);

    /// <summary>Height 30% taller than the original 350 (455) -- more room for the grid now that cells are smaller (see InventoryGridContent.CellSize). Width is no longer fixed -- see WindowWidthFraction.</summary>
    public static float WindowHeight = 455f;

    /// <summary>Both windows take up this fraction of the map window's own width, side by side.</summary>
    public static float WindowWidthFraction = 0.33f;
}
