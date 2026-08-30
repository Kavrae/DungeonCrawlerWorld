using Game.Modules.Actions.Activators;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class WandActivationEffectsTests
{
    [TestMethod]
    public void ComputeMaxCharges_IntelligenceTotal1_ReturnsMinCharges()
    {
        Assert.AreEqual(WandActivationEffects.MinCharges, WandActivationEffects.ComputeMaxCharges(1));
    }

    [TestMethod]
    public void ComputeMaxCharges_IntelligenceTotal300_ReturnsMaxCharges()
    {
        Assert.AreEqual(WandActivationEffects.MaxCharges, WandActivationEffects.ComputeMaxCharges(300));
    }

    [TestMethod]
    public void ComputeMaxCharges_BelowMinimum_ClampsToIntelligenceTotal1Result()
    {
        Assert.AreEqual(WandActivationEffects.ComputeMaxCharges(1), WandActivationEffects.ComputeMaxCharges(0));
    }

    [TestMethod]
    public void ComputeMaxCharges_AboveMaximum_ClampsToIntelligenceTotal300Result()
    {
        Assert.AreEqual(WandActivationEffects.ComputeMaxCharges(300), WandActivationEffects.ComputeMaxCharges(301));
    }

    [TestMethod]
    public void ComputeMaxCharges_MidRangeIntelligence_IsBetweenMinAndMaxCharges()
    {
        var midCharges = WandActivationEffects.ComputeMaxCharges(150);

        Assert.IsGreaterThan(WandActivationEffects.MinCharges, midCharges);
        Assert.IsLessThan(WandActivationEffects.MaxCharges, midCharges);
    }
}
