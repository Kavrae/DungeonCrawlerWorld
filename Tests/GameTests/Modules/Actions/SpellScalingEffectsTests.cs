using Game.Modules.Actions;
using Engine.Tiles;
using Game.Modules.Actions.Activators;

namespace Tests.GameTests.Modules.Actions;

[TestClass]
public sealed class SpellScalingEffectsTests
{
    [TestMethod]
    public void ComputeScaleMultiplier_IntelligenceTotal1_ReturnsOne()
    {
        Assert.AreEqual(1.0f, SpellScalingEffects.ComputeScaleMultiplier(1));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_IntelligenceTotal300_ReturnsFour()
    {
        Assert.AreEqual(4.0f, SpellScalingEffects.ComputeScaleMultiplier(300));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_BelowMinimum_ClampsToIntelligenceTotal1Result()
    {
        Assert.AreEqual(SpellScalingEffects.ComputeScaleMultiplier(1), SpellScalingEffects.ComputeScaleMultiplier(0));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_AboveMaximum_ClampsToIntelligenceTotal300Result()
    {
        Assert.AreEqual(SpellScalingEffects.ComputeScaleMultiplier(300), SpellScalingEffects.ComputeScaleMultiplier(301));
    }

    [TestMethod]
    public void ScaleTargeting_ScalesRangeAndAreaSize_RoundedToNearestInt()
    {
        var baseTargeting = new TargetingSpec(TargetShape.Circle, MaximumRange: 5, AreaSize: 3);

        var scaled = SpellScalingEffects.ScaleTargeting(baseTargeting, 4.0f);

        Assert.AreEqual(20, scaled.MaximumRange);
        Assert.AreEqual(12, scaled.AreaSize);
        Assert.AreEqual(TargetShape.Circle, scaled.Shape);
    }

    [TestMethod]
    public void ScaleTargeting_MultiplierOfOne_LeavesTargetingUnchanged()
    {
        var baseTargeting = new TargetingSpec(TargetShape.Adjacent, MaximumRange: 0, AreaSize: 0, Anchor: TargetAnchor.Caster);

        var scaled = SpellScalingEffects.ScaleTargeting(baseTargeting, 1.0f);

        Assert.AreEqual(baseTargeting, scaled);
    }
}
