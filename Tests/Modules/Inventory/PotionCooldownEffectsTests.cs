using Game.Modules.Inventory;

namespace Tests.Modules.Inventory;

[TestClass]
public sealed class PotionCooldownEffectsTests
{
    [TestMethod]
    public void ComputeDurationFrames_ConstitutionTotal1_ReturnsDurationFrames()
    {
        Assert.AreEqual(PotionCooldownEffects.DurationFrames, PotionCooldownEffects.ComputeDurationFrames(1));
    }

    [TestMethod]
    public void ComputeDurationFrames_ConstitutionTotal300_ReturnsMinDurationFrames()
    {
        Assert.AreEqual(PotionCooldownEffects.MinDurationFrames, PotionCooldownEffects.ComputeDurationFrames(300));
    }

    [TestMethod]
    public void ComputeDurationFrames_BelowMinimum_ClampsToConstitutionTotal1Result()
    {
        Assert.AreEqual(PotionCooldownEffects.ComputeDurationFrames(1), PotionCooldownEffects.ComputeDurationFrames(0));
    }

    [TestMethod]
    public void ComputeDurationFrames_AboveMaximum_ClampsToConstitutionTotal300Result()
    {
        Assert.AreEqual(PotionCooldownEffects.ComputeDurationFrames(300), PotionCooldownEffects.ComputeDurationFrames(301));
    }

    [TestMethod]
    public void ComputeAbusePoisonDurationTicks_DerivesFromGivenDurationFrames_NotAConstant()
    {
        var maxDurationTicks = PotionCooldownEffects.ComputeAbusePoisonDurationTicks(PotionCooldownEffects.DurationFrames);
        var minDurationTicks = PotionCooldownEffects.ComputeAbusePoisonDurationTicks(PotionCooldownEffects.MinDurationFrames);

        Assert.IsTrue(minDurationTicks < maxDurationTicks);
    }
}
