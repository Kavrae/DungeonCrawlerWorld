using Engine.Math;
using Game.Modules.Actions.Activators;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ScrollScalingEffectsTests
{
    [TestMethod]
    public void ComputeScaleMultiplier_IntelligenceTotal1_ReturnsOne()
    {
        Assert.AreEqual(1.0f, ScrollScalingEffects.ComputeScaleMultiplier(1));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_IntelligenceTotal300_ReturnsFour()
    {
        Assert.AreEqual(4.0f, ScrollScalingEffects.ComputeScaleMultiplier(300));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_BelowMinimum_ClampsToIntelligenceTotal1Result()
    {
        Assert.AreEqual(ScrollScalingEffects.ComputeScaleMultiplier(1), ScrollScalingEffects.ComputeScaleMultiplier(0));
    }

    [TestMethod]
    public void ComputeScaleMultiplier_AboveMaximum_ClampsToIntelligenceTotal300Result()
    {
        Assert.AreEqual(ScrollScalingEffects.ComputeScaleMultiplier(300), ScrollScalingEffects.ComputeScaleMultiplier(301));
    }

    [TestMethod]
    public void ScaleTargeting_ScalesRangeAndAreaSize_RoundedToNearestInt()
    {
        var baseTargeting = new TargetingSpec(TargetShape.Burst, Range: 5, AreaSize: 3);

        var scaled = ScrollScalingEffects.ScaleTargeting(baseTargeting, 4.0f);

        Assert.AreEqual(20, scaled.Range);
        Assert.AreEqual(12, scaled.AreaSize);
        Assert.AreEqual(TargetShape.Burst, scaled.Shape);
    }

    [TestMethod]
    public void ScaleTargeting_MultiplierOfOne_LeavesTargetingUnchanged()
    {
        var baseTargeting = new TargetingSpec(TargetShape.Adjacent, Range: 0, AreaSize: 0);

        var scaled = ScrollScalingEffects.ScaleTargeting(baseTargeting, 1.0f);

        Assert.AreEqual(baseTargeting, scaled);
    }
}
