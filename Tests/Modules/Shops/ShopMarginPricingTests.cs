using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.Tags;

namespace Tests.Modules.Shops;

[TestClass]
public sealed class ShopMarginPricingTests
{
    private const int PlayerEntityId = 0;

    private static readonly ShopComponent GeneralShop = new(acceptedItems: null, buyMultiplier: 1.20f, sellMultiplier: 0.80f);

    [TestMethod]
    public void ComputeMarginReductionFraction_Charisma1_ReturnsZero() =>
        Assert.AreEqual(0f, ShopMarginPricing.ComputeMarginReductionFraction(charismaTotal: 1));

    [TestMethod]
    public void ComputeMarginReductionFraction_Charisma300_ReturnsHalf() =>
        Assert.AreEqual(0.5f, ShopMarginPricing.ComputeMarginReductionFraction(charismaTotal: 300), delta: 0.0001f);

    [TestMethod]
    [DataRow(0f, 1.20f, 0.80f, DisplayName = "ApplyMarginReduction_NoReduction_MultiplierUnchanged")]
    [DataRow(0.5f, 1.10f, 0.90f, DisplayName = "ApplyMarginReduction_HalfReduction_HalvesTheMarginOnBothSidesOfParity")]
    [DataRow(1f, 1.0f, 1.0f, DisplayName = "ApplyMarginReduction_FullReduction_MultiplierReachesExactParity")]
    public void ApplyMarginReduction_MovesBothMultipliersTowardParity(float reductionFraction, float expectedBuyMultiplier, float expectedSellMultiplier)
    {
        Assert.AreEqual(expectedBuyMultiplier, ShopMarginPricing.ApplyMarginReduction(1.20f, reductionFraction), delta: 0.0001f);
        Assert.AreEqual(expectedSellMultiplier, ShopMarginPricing.ApplyMarginReduction(0.80f, reductionFraction), delta: 0.0001f);
    }

    [TestMethod]
    public void ResolveEffectiveShop_PlayerHasNoCharismaComponent_ReturnsShopUnchanged()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4));

        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, GeneralShop, PlayerEntityId);

        Assert.AreEqual(GeneralShop.BuyMultiplier, effectiveShop.BuyMultiplier);
        Assert.AreEqual(GeneralShop.SellMultiplier, effectiveShop.SellMultiplier);
    }

    [TestMethod]
    public void ResolveEffectiveShop_Charisma1_ReturnsShopUnchanged()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4));
        manager.GetPackedPool<AbilityScoresComponent>().Add(PlayerEntityId, AbilityScoreTestPools.Score(AbilityScoreType.Charisma, baseValue: 1, total: 1));

        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, GeneralShop, PlayerEntityId);

        Assert.AreEqual(GeneralShop.BuyMultiplier, effectiveShop.BuyMultiplier);
        Assert.AreEqual(GeneralShop.SellMultiplier, effectiveShop.SellMultiplier);
    }

    [TestMethod]
    public void ResolveEffectiveShop_Charisma300_HalvesTheMarginAndPreservesAcceptedItems()
    {
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4));
        manager.GetPackedPool<AbilityScoresComponent>().Add(PlayerEntityId, AbilityScoreTestPools.Score(AbilityScoreType.Charisma, baseValue: 300, total: 300));

        var potionShop = new ShopComponent(acceptedItems: Engine.Tags.GameplayTagQuery.Any([GameTags.ItemConsumablePotion]), buyMultiplier: 1.20f, sellMultiplier: 0.80f);
        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, potionShop, PlayerEntityId);

        Assert.AreEqual(1.10f, effectiveShop.BuyMultiplier, delta: 0.0001f);
        Assert.AreEqual(0.90f, effectiveShop.SellMultiplier, delta: 0.0001f);
        Assert.AreSame(potionShop.AcceptedItems, effectiveShop.AcceptedItems);
    }
}
