using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for InventoryWindowController's own button and the
/// InventoryManagementWindow it opens -- see HudChrome's own doc comment for why these are plain
/// mutable fields rather than readonly.</summary>
public static class InventoryChrome
{
    /// <summary>Between this button's own top edge and HealthWindowChrome's heart button sitting directly above it -- mirrors HealthWindowChrome.NotificationClearanceGap's own reasoning, one button-stack step down.</summary>
    public static Vector2 ButtonGap = new(0, 8);

    public static Vector2 ButtonPosition = HealthWindowChrome.ButtonPosition + new Vector2(0, HealthWindowChrome.ButtonSize.Y) + ButtonGap;

    /// <summary>Same as HealthWindowChrome.ButtonSize (HotbarContent.SlotSize), so the three HUD-trigger buttons (Health, Inventory, Ability Score) read as one consistent vertical stack, sized like the hotbar since each now also carries its own hotbar-style HotkeyLabel overlay.</summary>
    public static Vector2 ButtonSize = HotbarContent.SlotSize;

    /// <summary>Same value as HealthWindowChrome.WindowPosition today, but kept as its own independent field -- two separately-owned windows that happen to coincide, not one true duplicate.</summary>
    public static Vector2 WindowPosition = new(300, 150);

    /// <summary>Height 30% taller than the original 350 (455) -- more room for the grid now that cells are smaller (see InventoryGridContent.CellSize). Width is no longer fixed -- see WindowWidthFraction.</summary>
    public static float WindowHeight = 455f;

    /// <summary>Both windows take up this fraction of the map window's own width, side by side.</summary>
    public static float WindowWidthFraction = 0.33f;
}
