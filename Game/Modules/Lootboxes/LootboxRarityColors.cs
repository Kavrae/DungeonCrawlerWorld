using Microsoft.Xna.Framework;

namespace Game.Modules.Lootboxes;

/// <summary>The color that identifies each loot box rarity.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class LootboxRarityColors
{
    public static Color For(LootboxRarity rarity) => rarity switch
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
