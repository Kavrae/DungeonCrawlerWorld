namespace Game.Modules.Shops.Components;

/// <summary>
/// Marks an entity as a shop and carries its trading rules: AllowedTags null means "buys/sells
/// anything" (General Shop); a non-null list restricts trading to items carrying at least one of
/// those tags (Potion Shop: [Tag.Potion]). BuyMultiplier/SellMultiplier are applied to an item's
/// own ItemDefinition.Value to get the shop's actual price (see ShopActions.ComputeBuyPrice/
/// ComputeSellPrice) -- BuyMultiplier > 1 (player pays more than Value), SellMultiplier &lt; 1
/// (player receives less than Value), leaving room for a future Charisma/skill-based reduction to
/// narrow that spread.
/// </summary>
public readonly struct ShopComponent(IReadOnlyList<Tag>? allowedTags, float buyMultiplier, float sellMultiplier)
{
    public IReadOnlyList<Tag>? AllowedTags { get; } = allowedTags;
    public float BuyMultiplier { get; } = buyMultiplier;
    public float SellMultiplier { get; } = sellMultiplier;

    public override readonly string ToString() => $"AllowedTags : {(AllowedTags is null ? "Any" : string.Join(", ", AllowedTags))}\nBuyMultiplier : {BuyMultiplier}\nSellMultiplier : {SellMultiplier}";
}
