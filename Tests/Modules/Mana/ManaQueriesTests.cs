using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Mana;

[TestClass]
public sealed class ManaQueriesTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<StatModifierComponent> DoubledMaximumMana()
    {
        var statModifiers = EmptyPools.Multi<StatModifierComponent>();
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumMana, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, 1f, FrameDeadline.Never, ActionSource.Admin));
        return statModifiers;
    }

    [TestMethod]
    public void TryGetTotals_MaximumManaModifier_GivesTheModifiedMaximum()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 15, baseMaximumMana: 10));

        var found = ManaQueries.TryGetTotals(pool, DoubledMaximumMana(), 0, out var current, out var maximum);

        Assert.IsTrue(found);
        Assert.AreEqual(15f, current);
        Assert.AreEqual(20f, maximum);
    }

    [TestMethod]
    public void TryGetBaseMaximum_MaximumManaModifier_GivesTheBaseUntouched()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 15, baseMaximumMana: 10));

        Assert.IsTrue(ManaQueries.TryGetBaseMaximum(pool, 0, out var baseMaximum));
        Assert.AreEqual(10f, baseMaximum);
    }

    [TestMethod]
    public void BothReads_EntityWithNoMana_ReturnFalse()
    {
        var pool = CreatePool();

        Assert.IsFalse(ManaQueries.TryGetTotals(pool, DoubledMaximumMana(), 0, out _, out _));
        Assert.IsFalse(ManaQueries.TryGetBaseMaximum(pool, 0, out _));
    }
}
