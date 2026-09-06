namespace Game.Modules.Shops.Components;

/// <summary>
/// Marks an entity as a shop and carries its trading rules: AllowedTags null means "buys/sells
/// anything" (General Shop); a non-null list restricts trading to items carrying at least one of
/// those tags (Potion Shop: [Tag.Potion]). BuyMultiplier/SellMultiplier are the shop's own
/// *configured* margin -- applied to an item's own ItemDefinition.GoldValue to get its actual price
/// (see ShopActions.ComputeBuyPrice/ComputeSellPrice) -- BuyMultiplier > 1 (player pays more than
/// GoldValue), SellMultiplier &lt; 1 (player receives less). ShopMarginPricing.ResolveEffectiveShop
/// narrows that spread per-trade based on the buyer/seller's own Charisma (and, eventually, a
/// shopping-skill system) -- these two properties always stay the shop's own unmodified baseline.
/// </summary>
public readonly struct ShopComponent(IReadOnlyList<Tag>? allowedTags, float buyMultiplier, float sellMultiplier)
{
    public IReadOnlyList<Tag>? AllowedTags { get; } = allowedTags;
    public float BuyMultiplier { get; } = buyMultiplier;
    public float SellMultiplier { get; } = sellMultiplier;

    public override readonly string ToString() => $"AllowedTags : {(AllowedTags is null ? "Any" : string.Join(", ", AllowedTags))}\nBuyMultiplier : {BuyMultiplier}\nSellMultiplier : {SellMultiplier}";
}
