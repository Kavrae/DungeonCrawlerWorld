using Engine.ECS.Components.Stores;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class StatusEffectQueriesTests
{
    private static MultiComponentPool<StatusEffectStack> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    [TestMethod]
    public void HasStack_NoEntries_ReturnsFalse()
    {
        var pool = CreatePool();

        Assert.IsFalse(StatusEffectQueries.HasStack(pool, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void HasStack_MatchingEntry_ReturnsTrue()
    {
        var pool = CreatePool();
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));

        Assert.IsTrue(StatusEffectQueries.HasStack(pool, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void CountStacks_CountsEveryEntryForThatEntityAndType()
    {
        var pool = CreatePool();
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.FromEntity(42)));

        Assert.AreEqual(2, StatusEffectQueries.CountStacks(pool, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void CountStacks_DifferentEntity_IsIndependent()
    {
        var pool = CreatePool();
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(pool, 1, StatusEffectType.Burning));
    }

    [TestMethod]
    public void GetActiveEffectTypes_NoEntries_FillsEmpty()
    {
        var pool = CreatePool();
        var destination = new List<StatusEffectType> { StatusEffectType.Burning };

        StatusEffectQueries.GetActiveEffectTypes(pool, 0, destination);

        Assert.AreEqual(0, destination.Count);
    }

    [TestMethod]
    public void GetActiveEffectTypes_MultipleStacksOfSameType_ReturnsTypeOnlyOnce()
    {
        var pool = CreatePool();
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        pool.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.FromEntity(42)));
        var destination = new List<StatusEffectType>();

        StatusEffectQueries.GetActiveEffectTypes(pool, 0, destination);

        CollectionAssert.AreEqual(new[] { StatusEffectType.Burning }, destination);
    }
}
