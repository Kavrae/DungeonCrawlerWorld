using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class BodyPartSelectionTests
{
    private static MultiComponentPool<BodyPartComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    [TestMethod]
    public void PickRandom_RepeatedSeededRolls_AlwaysLandsOnEntityOwnDenseIndex()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 10, 10, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 20, 20, isVital: true));
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, 15, 15, isVital: false));
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            var denseIndex = BodyPartSelection.PickRandom(pool, 0, mathUtility);

            Assert.IsGreaterThanOrEqualTo(0, denseIndex);
            Assert.AreEqual(0, pool.GetEntityIdByDenseIndex(denseIndex));
        }
    }

    [TestMethod]
    public void PickRandom_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var pool = CreatePool();
        var mathUtility = new MathUtility(new Random(1));

        var denseIndex = BodyPartSelection.PickRandom(pool, 0, mathUtility);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickLowestPercentage_MixedFractions_PicksLowestFractionPart()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 9, maximumHealth: 10, isVital: true)); // 90%
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 5, maximumHealth: 20, isVital: true)); // 25%
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, currentHealth: 10, maximumHealth: 15, isVital: false)); // ~67%

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickLowestPercentage_LowestPartLockedOut_SkipsItForNextLowest()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 9, maximumHealth: 10, isVital: true)); // 90%
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, currentHealth: 10, maximumHealth: 15, isVital: false)); // ~67%
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 5, maximumHealth: 20, isVital: true)); // 25%, locked out below.

        var torsoDenseIndex = FindDenseIndexByName(pool, 0, "Torso");
        pool.UpdateByDenseIndex(torsoDenseIndex, static (ref BodyPartComponent part) => part.RegenLockoutFramesRemaining = 100);

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual("Arm", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    private static int FindDenseIndexByName(MultiComponentPool<BodyPartComponent> pool, int entityId, string name)
    {
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            if (pool.GetReadonlyByDenseIndex(denseIndex).Name == name)
            {
                return denseIndex;
            }
        }

        return -1;
    }

    [TestMethod]
    public void PickLowestPercentage_EveryPartFullOrLockedOut_ReturnsNegativeOne()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 10, maximumHealth: 10, isVital: true)); // Full.
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, currentHealth: 5, maximumHealth: 20, isVital: true)); // Damaged but locked out.
        var lockedDenseIndex = pool.GetFirstDenseIndex(0);
        pool.UpdateByDenseIndex(lockedDenseIndex, static (ref BodyPartComponent part) => part.RegenLockoutFramesRemaining = 50);

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickLowestPercentage_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var pool = CreatePool();

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickLowestPercentage_PartAtRawMaximumWithActiveBuff_StillSelectableUpToTheEffectiveMaximum()
    {
        var pool = CreatePool();
        // At its raw maximum (100% by that measure), but a +50% MaximumHealth buff means its real
        // cap is 60 -- this part still has headroom and must not be treated as "already full."
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 40, maximumHealth: 40, isVital: true));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0, statModifiers);

        Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickLowestPercentage_PartAtItsEffectiveMaximumWithActiveBuff_NotSelected()
    {
        var pool = CreatePool();
        // 60/60 with the same +50% buff active (effective maximum is 60) -- genuinely full, unlike the case above.
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, currentHealth: 60, maximumHealth: 40, isVital: true));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0, statModifiers);

        Assert.AreEqual(-1, denseIndex);
    }
}
