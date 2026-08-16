using Engine.ECS.Components.Stores;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.StatModifiers;

[TestClass]
public sealed class StatModifierMathTests
{
    private static MultiComponentPool<StatModifierComponent> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    private static StatModifierComponent Modifier(StatModifierTarget target, StatModifierOperation operation, float magnitude) =>
        new(target, operation, StatModifierPolarity.Buff, canModify: false, magnitude, null, StatusEffectSource.Admin);

    [TestMethod]
    public void GetEffectiveValues_NoPool_ReturnsBothBaseValuesUnchanged()
    {
        var destination = new float[2];

        StatModifierMath.GetEffectiveValues(null, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination);

        Assert.AreEqual(10f, destination[0]);
        Assert.AreEqual(200f, destination[1]);
    }

    [TestMethod]
    public void GetEffectiveValues_NoModifiers_ReturnsBothBaseValuesUnchanged()
    {
        var pool = CreatePool();
        var destination = new float[2];

        StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination);

        Assert.AreEqual(10f, destination[0]);
        Assert.AreEqual(200f, destination[1]);
    }

    [TestMethod]
    public void GetEffectiveValues_MatchesGetEffectiveValue_ForEachTargetIndependently_AdditiveOnly()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, 5f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, 50f));
        var destination = new float[2];

        StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), destination[0]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), destination[1]);
    }

    [TestMethod]
    public void GetEffectiveValues_MatchesGetEffectiveValue_ForEachTargetIndependently_MultiplicativeOnly()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, 1f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, -0.5f));
        var destination = new float[2];

        StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), destination[0]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), destination[1]);
    }

    [TestMethod]
    public void GetEffectiveValues_MatchesGetEffectiveValue_BothAdditiveAndMultiplicative()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, 5f));
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, 1f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, 50f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, -0.25f));
        // A modifier targeting neither -- must not leak into either accumulator.
        pool.Add(0, Modifier(StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, 999f));
        var destination = new float[2];

        StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), destination[0]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), destination[1]);
    }

    [TestMethod]
    public void GetEffectiveValues_OnePair_MatchesGetEffectiveValue()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, 5f));
        var destination = new float[1];

        StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f)], destination);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), destination[0]);
    }

    [TestMethod]
    public void GetEffectiveValues_FivePairs_ComputesEachIndependently()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, 5f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, 50f));
        pool.Add(0, Modifier(StatModifierTarget.ManaRegen, StatModifierOperation.Multiplicative, 1f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumMana, StatModifierOperation.Multiplicative, -0.5f));
        pool.Add(0, Modifier(StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, 2f));
        var destination = new float[5];

        StatModifierMath.GetEffectiveValues(
            pool,
            0,
            [
                (StatModifierTarget.HealthRegen, 10f),
                (StatModifierTarget.MaximumHealth, 200f),
                (StatModifierTarget.ManaRegen, 5f),
                (StatModifierTarget.MaximumMana, 100f),
                (StatModifierTarget.IncomingDamage, 0f),
            ],
            destination);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), destination[0]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), destination[1]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.ManaRegen, 5f), destination[2]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumMana, 100f), destination[3]);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.IncomingDamage, 0f), destination[4]);
    }

    [TestMethod]
    public void GetEffectiveValues_MismatchedLengths_Throws()
    {
        var pool = CreatePool();
        var destination = new float[1];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            StatModifierMath.GetEffectiveValues(pool, 0, [(StatModifierTarget.HealthRegen, 10f), (StatModifierTarget.MaximumHealth, 200f)], destination));
    }
}
