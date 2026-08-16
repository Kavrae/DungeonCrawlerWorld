using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

[TestClass]
public sealed class AbilityScoreMathTests
{
    private static MultiComponentPool<StatModifierComponent> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    private static StatModifierComponent Modifier(StatModifierTarget target, StatModifierOperation operation, float magnitude) =>
        new(target, operation, StatModifierPolarity.Buff, canModify: false, magnitude, null, StatusEffectSource.Admin);

    [TestMethod]
    public void ClampBaseValue_BelowMinimum_ClampsToOne()
    {
        Assert.AreEqual((ushort)1, AbilityScoreMath.ClampBaseValue(0));
    }

    [TestMethod]
    public void ClampBaseValue_AboveMaximum_ClampsToThreeHundred()
    {
        Assert.AreEqual((ushort)300, AbilityScoreMath.ClampBaseValue(301));
    }

    [TestMethod]
    public void ClampBaseValue_WithinRange_Unchanged()
    {
        Assert.AreEqual((ushort)5, AbilityScoreMath.ClampBaseValue(5));
    }

    [TestMethod]
    public void ComputeTotal_NoPool_ReturnsBaseValueUnchanged()
    {
        var total = AbilityScoreMath.ComputeTotal(null, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual((ushort)5, total);
    }

    [TestMethod]
    public void ComputeTotal_AdditiveModifier_AddsToBase()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.Strength, StatModifierOperation.Additive, 3f));

        var total = AbilityScoreMath.ComputeTotal(pool, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual((ushort)8, total);
    }

    [TestMethod]
    public void ComputeTotal_MultiplicativeModifier_ScalesBase()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.Strength, StatModifierOperation.Multiplicative, 1f));

        var total = AbilityScoreMath.ComputeTotal(pool, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual((ushort)10, total);
    }

    [TestMethod]
    public void ComputeTotal_ModifierTargetingDifferentType_IsIgnored()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.Dexterity, StatModifierOperation.Additive, 100f));

        var total = AbilityScoreMath.ComputeTotal(pool, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual((ushort)5, total);
    }

    [TestMethod]
    public void ComputeTotal_LargeNegativeModifier_FloorsAtZero()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.Strength, StatModifierOperation.Additive, -1000f));

        var total = AbilityScoreMath.ComputeTotal(pool, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual((ushort)0, total);
    }

    [TestMethod]
    public void ComputeTotal_LargePositiveModifier_ClampsToShortMaxValue()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.Strength, StatModifierOperation.Additive, 1_000_000f));

        var total = AbilityScoreMath.ComputeTotal(pool, 0, AbilityScoreType.Strength, 5);

        Assert.AreEqual(ushort.MaxValue, total);
    }

    [TestMethod]
    public void ToStatModifierTarget_AndFromStatModifierTarget_AreInverses()
    {
        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            var target = AbilityScoreMath.ToStatModifierTarget(type);
            Assert.AreEqual(type, AbilityScoreMath.FromStatModifierTarget(target));
        }
    }

    [TestMethod]
    public void FromStatModifierTarget_NonAbilityScoreTarget_ReturnsNull()
    {
        Assert.IsNull(AbilityScoreMath.FromStatModifierTarget(StatModifierTarget.MaximumHealth));
    }
}
