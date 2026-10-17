namespace Game.Modules.Lootboxes;

/// <summary>Represents the rarity of a lootbox.</summary>
/// <remarks>Reward value scales with lootbox rarity</remarks>
/// <cleanupVersion>1</cleanupVersion>
public enum LootboxRarity : byte
{
    Bronze,
    Silver,
    Gold,
    Platinum,
    Legendary,
    Celestial
}
