namespace Game.Modules.Achievements;

/// <summary>Represents the reward for earning an achievement, specifically a lootbox.</summary>
/// <remarks>TEMPORARY does not contain the actual reward that is granted upon opening the lootbox</remarks>
/// <param name="Rarity">The rarity of the lootbox.</param>
/// <param name="BoxType">The type of the lootbox.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record Lootbox(LootboxRarity Rarity, string BoxType)
{
    public string DisplayLabel => $"{Rarity} {BoxType} Box";
}
