using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>Position/sizing constants for the InventoryManagementWindow InventoryWindowController opens -- see HudChrome's own doc comment for why these are plain
/// mutable fields rather than readonly.</summary>
public static class InventoryChrome
{
    /// <summary>Same value as HealthWindowChrome.WindowPosition today, but kept as its own independent field -- two separately-owned windows that happen to coincide, not one true duplicate.</summary>
    public static Vector2 WindowPosition = new(300, 150);

    /// <summary>Height 30% taller than the original 350 (455) -- more room for the grid now that cells are smaller (see InventoryGridContent.CellSize).</summary>
    public static float WindowHeight = 455f;

    /// <summary>The Inventory and Actions windows' width: 15 icons or 4 cards a row (InventoryGridContent.CellSize, ShopCellWidthMultiplier), whether or not the list shows its scrollbar.</summary>
    public static float WindowWidth = 613f;
}
