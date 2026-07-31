namespace Game.Modules.Achievements;

/// <summary>
/// Data-only stub: an achievement can name the lootbox it grants, but nothing delivers its
/// contents yet -- see TODO.md's "Achievement lootbox delivery" entry, blocked on the
/// Inventory system not existing. DisplayLabel is what the achievement notification shows
/// (e.g. "Bronze Adventurer Box").
/// </summary>
public sealed record LootboxReward(LootboxRarity Rarity, string BoxType)
{
    public string DisplayLabel => $"{Rarity} {BoxType} Box";
}
