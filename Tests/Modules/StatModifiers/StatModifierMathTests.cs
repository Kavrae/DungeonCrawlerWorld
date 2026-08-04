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
        new(target, operation, StatModifierPolarity.Buff, canModify: false, magnitude, StatModifierComponent.Permanent, StatusEffectSource.Admin);

    [TestMethod]
    public void GetEffectiveValues_NoPool_ReturnsBothBaseValuesUnchanged()
    {
        var (first, second) = StatModifierMath.GetEffectiveValues(null, 0, StatModifierTarget.HealthRegen, 10f, StatModifierTarget.MaximumHealth, 200f);

        Assert.AreEqual(10f, first);
        Assert.AreEqual(200f, second);
    }

    [TestMethod]
    public void GetEffectiveValues_NoModifiers_ReturnsBothBaseValuesUnchanged()
    {
        var pool = CreatePool();

        var (first, second) = StatModifierMath.GetEffectiveValues(pool, 0, StatModifierTarget.HealthRegen, 10f, StatModifierTarget.MaximumHealth, 200f);

        Assert.AreEqual(10f, first);
        Assert.AreEqual(200f, second);
    }

    [TestMethod]
    public void GetEffectiveValues_MatchesGetEffectiveValue_ForEachTargetIndependently_AdditiveOnly()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, 5f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Additive, 50f));

        var (first, second) = StatModifierMath.GetEffectiveValues(pool, 0, StatModifierTarget.HealthRegen, 10f, StatModifierTarget.MaximumHealth, 200f);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), first);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), second);
    }

    [TestMethod]
    public void GetEffectiveValues_MatchesGetEffectiveValue_ForEachTargetIndependently_MultiplicativeOnly()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, 1f));
        pool.Add(0, Modifier(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, -0.5f));

        var (first, second) = StatModifierMath.GetEffectiveValues(pool, 0, StatModifierTarget.HealthRegen, 10f, StatModifierTarget.MaximumHealth, 200f);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), first);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), second);
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

        var (first, second) = StatModifierMath.GetEffectiveValues(pool, 0, StatModifierTarget.HealthRegen, 10f, StatModifierTarget.MaximumHealth, 200f);

        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.HealthRegen, 10f), first);
        Assert.AreEqual(StatModifierMath.GetEffectiveValue(pool, 0, StatModifierTarget.MaximumHealth, 200f), second);
    }
}
