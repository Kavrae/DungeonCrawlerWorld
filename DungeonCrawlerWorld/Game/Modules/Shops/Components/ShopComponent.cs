using Engine.Tags;

namespace Game.Modules.Shops.Components;

/// <summary>
/// Marks an entity as a shop and carries its trading rules: AcceptedItems null means "buys/sells
/// anything" (General Shop); otherwise trading is restricted to items whose tags match it (Potion
/// Shop: any of Item.Consumable.Potion). BuyMultiplier/SellMultiplier are the shop's own
/// *configured* margin -- applied to an item's own ItemDefinition.GoldValue to get its actual price
/// (see ShopActions.ComputeBuyPrice/ComputeSellPrice) -- BuyMultiplier > 1 (player pays more than
/// GoldValue), SellMultiplier &lt; 1 (player receives less). ShopMarginPricing.ResolveEffectiveShop
/// narrows that spread per-trade based on the buyer/seller's own Charisma (and, eventually, a
/// shopping-skill system) -- these two properties always stay the shop's own unmodified baseline.
/// </summary>
/// <remarks>AcceptedItems is shared: every shop of one kind holds the same query instance.</remarks>
public readonly struct ShopComponent(GameplayTagQuery? acceptedItems, float buyMultiplier, float sellMultiplier)
{
    public GameplayTagQuery? AcceptedItems { get; } = acceptedItems;
    public float BuyMultiplier { get; } = buyMultiplier;
    public float SellMultiplier { get; } = sellMultiplier;

    public override readonly string ToString() => $"AcceptedItems : {(AcceptedItems is null ? "Any" : AcceptedItems.ToString())}\nBuyMultiplier : {BuyMultiplier}\nSellMultiplier : {SellMultiplier}";
}
