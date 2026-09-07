using Game.Modules.Actions.Activators;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class DodgeEffectsTests
{
    [TestMethod]
    public void ComputeWindowFrames_DexterityTotal1_ReturnsWindowFrames()
    {
        Assert.AreEqual(DodgeEffects.WindowFrames, DodgeEffects.ComputeWindowFrames(1));
    }

    [TestMethod]
    public void ComputeWindowFrames_DexterityTotal300_ReturnsMaxWindowFrames()
    {
        Assert.AreEqual(DodgeEffects.MaxWindowFrames, DodgeEffects.ComputeWindowFrames(300));
    }

    [TestMethod]
    public void ComputeWindowFrames_HigherDexterity_YieldsLongerWindow()
    {
        // Unlike PotionCooldownEffects (more Constitution shortens the cooldown), more Dexterity
        // is a benefit here -- a longer, more forgiving Dodge window. Assert.IsLessThan(upperBound,
        // value) checks value < upperBound (see PotionCooldownEffectsTests' own equivalent).
        Assert.IsLessThan(DodgeEffects.ComputeWindowFrames(300), DodgeEffects.ComputeWindowFrames(150));
    }

    [TestMethod]
    public void ComputeWindowFrames_BelowMinimum_ClampsToDexterityTotal1Result()
    {
        Assert.AreEqual(DodgeEffects.ComputeWindowFrames(1), DodgeEffects.ComputeWindowFrames(0));
    }

    [TestMethod]
    public void ComputeWindowFrames_AboveMaximum_ClampsToDexterityTotal300Result()
    {
        Assert.AreEqual(DodgeEffects.ComputeWindowFrames(300), DodgeEffects.ComputeWindowFrames(301));
    }
}
