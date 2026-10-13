using Game.Modules.Lootboxes;
using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.UI.Chrome;

/// <summary>Sizing and colors for the loot box results window -- see HudChrome's own doc comment for why these are plain mutable fields rather than readonly.</summary>
public static class LootboxChrome
{
    /// <summary>How many reward cells fit across the window when it opens; the grid re-flows to whatever width the player resizes it to.</summary>
    public static int ColumnsAtOpen = 8;

    /// <summary>Height of each section's "Bronze Adventurer Box x3" header.</summary>
    public static float HeaderHeight = 22f;

    /// <summary>Between the end of one section's cells and the next section's header.</summary>
    public static float SectionGap = 8f;

    /// <summary>The window never opens taller than this fraction of the map window's height; anything past it scrolls.</summary>
    public static float MaximumHeightFraction = 0.75f;

    /// <summary>The window's content width at open: exactly ColumnsAtOpen cells, plus room for the vertical scrollbar.</summary>
    public static float ContentWidthAtOpen => ColumnsAtOpen * (InventoryGridContent.CellSize.X + InventoryGridContent.CellGap) + ScrollbarAllowance;

    /// <summary>What the window adds around its content: title bar, border and padding.</summary>
    public static Vector2 WindowChromeAllowance = new(16, 40);

    /// <summary>Kept free beside the cells so a scrollbar appearing doesn't drop a column.</summary>
    public static float ScrollbarAllowance = 14f;

    /// <summary>The color that identifies a loot box rarity in text and chrome -- a section header's text, for one.</summary>
    /// <remarks>Never a sprite's: a box's look is its manifest entry's ("Lootbox-Bronze"), whose tint matches these today by choice, not by rule.</remarks>
    public static Color RarityColor(LootboxRarity rarity) => rarity switch
    {
        LootboxRarity.Bronze => new Color(205, 127, 50),
        LootboxRarity.Silver => new Color(192, 192, 192),
        LootboxRarity.Gold => new Color(255, 215, 0),
        LootboxRarity.Platinum => new Color(160, 230, 225),
        LootboxRarity.Legendary => new Color(255, 128, 0),
        LootboxRarity.Celestial => new Color(190, 140, 255),
        _ => Color.White,
    };
}
