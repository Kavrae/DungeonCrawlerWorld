using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;

namespace Tests.Modules.Shops;

[TestClass]
public sealed class ShopMarginPricingTests
{
    private const int PlayerEntityId = 0;

    private static readonly ShopComponent GeneralShop = new(allowedTags: null, buyMultiplier: 1.20f, sellMultiplier: 0.80f);

    [TestMethod]
    public void ComputeMarginReductionFraction_Charisma1_ReturnsZero() =>
        Assert.AreEqual(0f, ShopMarginPricing.ComputeMarginReductionFraction(charismaTotal: 1));

    [TestMethod]
    public void ComputeMarginReductionFraction_Charisma300_ReturnsHalf() =>
        Assert.AreEqual(0.5f, ShopMarginPricing.ComputeMarginReductionFraction(charismaTotal: 300), delta: 0.0001f);

    [TestMethod]
    public void ApplyMarginReduction_NoReduction_MultiplierUnchanged()
    {
        Assert.AreEqual(1.20f, ShopMarginPricing.ApplyMarginReduction(1.20f, reductionFraction: 0f), delta: 0.0001f);
        Assert.AreEqual(0.80f, ShopMarginPricing.ApplyMarginReduction(0.80f, reductionFraction: 0f), delta: 0.0001f);
    }

    [TestMethod]
    public void ApplyMarginReduction_HalfReduction_HalvesTheMarginOnBothSidesOfParity()
    {
        // BuyMultiplier's margin is (1.20 - 1) = 0.20 -- halved is 0.10, so 1.10.
        Assert.AreEqual(1.10f, ShopMarginPricing.ApplyMarginReduction(1.20f, reductionFraction: 0.5f), delta: 0.0001f);

        // SellMultiplier's margin is (1 - 0.80) = 0.20 -- halved is 0.10, so 0.90.
        Assert.AreEqual(0.90f, ShopMarginPricing.ApplyMarginReduction(0.80f, reductionFraction: 0.5f), delta: 0.0001f);
    }

    [TestMethod]
    public void ApplyMarginReduction_FullReduction_MultiplierReachesExactParity()
    {
        Assert.AreEqual(1.0f, ShopMarginPricing.ApplyMarginReduction(1.20f, reductionFraction: 1f), delta: 0.0001f);
        Assert.AreEqual(1.0f, ShopMarginPricing.ApplyMarginReduction(0.80f, reductionFraction: 1f), delta: 0.0001f);
    }

    [TestMethod]
    public void ResolveEffectiveShop_NoAbilityScorePoolRegistered_ReturnsShopUnchanged()
    {
        var manager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);

        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, GeneralShop, PlayerEntityId);

        Assert.AreEqual(GeneralShop.BuyMultiplier, effectiveShop.BuyMultiplier);
        Assert.AreEqual(GeneralShop.SellMultiplier, effectiveShop.SellMultiplier);
    }

    [TestMethod]
    public void ResolveEffectiveShop_PlayerHasNoCharismaComponent_ReturnsShopUnchanged()
    {
        var manager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        manager.RegisterMultiPool<AbilityScoreComponent>();

        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, GeneralShop, PlayerEntityId);

        Assert.AreEqual(GeneralShop.BuyMultiplier, effectiveShop.BuyMultiplier);
        Assert.AreEqual(GeneralShop.SellMultiplier, effectiveShop.SellMultiplier);
    }

    [TestMethod]
    public void ResolveEffectiveShop_Charisma1_ReturnsShopUnchanged()
    {
        var manager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        manager.RegisterMultiPool<AbilityScoreComponent>();
        manager.GetMultiPool<AbilityScoreComponent>().Add(PlayerEntityId, new AbilityScoreComponent(AbilityScoreType.Charisma, baseValue: 1, total: 1));

        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, GeneralShop, PlayerEntityId);

        Assert.AreEqual(GeneralShop.BuyMultiplier, effectiveShop.BuyMultiplier);
        Assert.AreEqual(GeneralShop.SellMultiplier, effectiveShop.SellMultiplier);
    }

    [TestMethod]
    public void ResolveEffectiveShop_Charisma300_HalvesTheMarginAndPreservesAllowedTags()
    {
        var manager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        manager.RegisterMultiPool<AbilityScoreComponent>();
        manager.GetMultiPool<AbilityScoreComponent>().Add(PlayerEntityId, new AbilityScoreComponent(AbilityScoreType.Charisma, baseValue: 300, total: 300));

        var potionShop = new ShopComponent(allowedTags: [Tag.Potion], buyMultiplier: 1.20f, sellMultiplier: 0.80f);
        var effectiveShop = ShopMarginPricing.ResolveEffectiveShop(manager, potionShop, PlayerEntityId);

        Assert.AreEqual(1.10f, effectiveShop.BuyMultiplier, delta: 0.0001f);
        Assert.AreEqual(0.90f, effectiveShop.SellMultiplier, delta: 0.0001f);
        CollectionAssert.AreEqual(new[] { Tag.Potion }, (System.Collections.ICollection)effectiveShop.AllowedTags!);
    }
}
