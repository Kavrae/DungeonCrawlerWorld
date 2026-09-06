using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Shops.Components;

namespace Game.Modules.Shops;

/// <summary>
/// Narrows a shop's own BuyMultiplier/SellMultiplier toward 1.0 (no markup/markdown) based on the
/// buyer/seller's Charisma -- the reduction ShopComponent's own doc comment already earmarked.
/// Charisma 1 leaves the shop's configured margin untouched; Charisma 300 halves it, reserving the
/// other half for a future shopping-skill system (TODO.md's planned 0-15 skill levels) to close --
/// see ComputeMarginReductionFraction's own doc comment for where that plugs in.
///
/// ResolveEffectiveShop is the one call every real price computation (ShopActions.
/// TryBuyFromShop/TrySellToShop, and every UI price/tooltip display in InventoryGridContent/
/// TradeWindow) makes before handing a ShopComponent to ShopStockPricing, so the stock-based band
/// math (Desperate/Understocked/Normal/Overstocked/Flooded) keeps working entirely unchanged --
/// just against a narrower base spread than the shop's own configured one.
/// </summary>
public static class ShopMarginPricing
{
    /// <summary>
    /// Charisma 1 -> 0 (no effect), Charisma 300 -> 0.5 (half the margin gone) -- reuses
    /// AbilityScoreMath.Lerp's existing [1,300] normalization every other ability-score consumer
    /// already relies on. A future shopping-skill contribution (its own 0..0.5 share) adds in here
    /// once that system exists, capped at 1.0 combined -- not wired yet, no skill system exists.
    /// </summary>
    public static float ComputeMarginReductionFraction(ushort charismaTotal) => AbilityScoreMath.Lerp(charismaTotal, 0f, 0.5f);

    /// <summary>1.0 plus reductionFraction's own share of (multiplier - 1) -- works identically for BuyMultiplier (&gt;1) and SellMultiplier (&lt;1) since margin is just multiplier-1 on either side of parity, and reductionFraction never pushes the result past 1.0 in the other direction.</summary>
    public static float ApplyMarginReduction(float multiplier, float reductionFraction) => 1f + (multiplier - 1f) * (1f - reductionFraction);

    /// <summary>
    /// The ShopComponent every real trade/display should price against instead of the raw one read
    /// off the shop entity -- same AllowedTags, BuyMultiplier/SellMultiplier narrowed by
    /// buyerOrSellerEntityId's own Charisma. Falls back to Charisma's own minimum (1, no reduction)
    /// if the entity has none or the AbilityScoreComponent pool isn't registered at all -- the same
    /// optional-pool tolerance ManaRegenSystem/SimpleHealthRegenSystem already extend to a missing
    /// ability-score setup.
    /// </summary>
    public static ShopComponent ResolveEffectiveShop(ComponentManager componentManager, ShopComponent shop, int buyerOrSellerEntityId)
    {
        var abilityScores = componentManager.GetOptionalMultiPool<AbilityScoreComponent>();
        var charismaTotal = abilityScores is not null && AbilityScoreQueries.TryGetComponent(abilityScores, buyerOrSellerEntityId, AbilityScoreType.Charisma, out var charisma)
            ? charisma.Total
            : AbilityScoreMath.MinimumBaseValue;

        var reductionFraction = ComputeMarginReductionFraction(charismaTotal);
        if (reductionFraction <= 0f)
        {
            return shop;
        }

        return new ShopComponent(shop.AllowedTags, ApplyMarginReduction(shop.BuyMultiplier, reductionFraction), ApplyMarginReduction(shop.SellMultiplier, reductionFraction));
    }
}
