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
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, 10, 10, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, 20, 20, isVital: true));
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, 0, 0, 15, 15, isVital: false));
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
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 9, maximumHealth: 10, isVital: true)); // 90%
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 5, maximumHealth: 20, isVital: true)); // 25%
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, 0, 0, currentHealth: 10, maximumHealth: 15, isVital: false)); // ~67%

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickLowestPercentage_LowestPartLockedOut_SkipsItForNextLowest()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 9, maximumHealth: 10, isVital: true)); // 90%
        pool.Add(0, new BodyPartComponent("Arm", BodyPartType.Arm, 0, 0, currentHealth: 10, maximumHealth: 15, isVital: false)); // ~67%
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 5, maximumHealth: 20, isVital: true)); // 25%, locked out below.

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
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 10, maximumHealth: 10, isVital: true)); // Full.
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 5, maximumHealth: 20, isVital: true)); // Damaged but locked out.
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
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 40, maximumHealth: 40, isVital: true));
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
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 60, maximumHealth: 40, isVital: true));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0, statModifiers);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickTopmost_MixedVerticalPositions_PicksHighestPosition()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Foot", BodyPartType.Foot, 0, verticalPosition: 0, 10, 10, isVital: false));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, verticalPosition: 4, 60, 60, isVital: true));
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 5, 30, 30, isVital: true));

        var denseIndex = BodyPartSelection.PickTopmost(pool, 0);

        Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickTopmost_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var pool = CreatePool();

        var denseIndex = BodyPartSelection.PickTopmost(pool, 0);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickBottommost_MixedVerticalPositions_PicksLowestPosition()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, verticalPosition: 4, 60, 60, isVital: true));
        pool.Add(0, new BodyPartComponent("Foot", BodyPartType.Foot, 0, verticalPosition: 0, 10, 10, isVital: false));

        var denseIndex = BodyPartSelection.PickBottommost(pool, 0);

        Assert.AreEqual("Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickBottommost_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var pool = CreatePool();

        var denseIndex = BodyPartSelection.PickBottommost(pool, 0);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickByType_MatchingTypePresent_ReturnsItsDenseIndex()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, 0, 0, 10, 10, isVital: false));

        var denseIndex = BodyPartSelection.PickByType(pool, 0, BodyPartType.Foot);

        Assert.AreEqual("Left Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByType_NoMatchingType_ReturnsNegativeOne()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, 30, 30, isVital: true));

        var denseIndex = BodyPartSelection.PickByType(pool, 0, BodyPartType.Foot);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypePresent_IgnoresFallback()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, 0, 0, 10, 10, isVital: false));
        var mathUtility = new MathUtility(new Random(1));

        var denseIndex = BodyPartSelection.PickByTypeWithFallback(pool, 0, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Topmost), mathUtility);

        Assert.AreEqual("Left Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_FallsBackToTopmost()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 4, 60, 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        var denseIndex = BodyPartSelection.PickByTypeWithFallback(pool, 0, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Topmost), mathUtility);

        Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_FallsBackToBottommost()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 4, 60, 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        var denseIndex = BodyPartSelection.PickByTypeWithFallback(pool, 0, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Bottommost), mathUtility);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_RandomFallback_AlwaysReturnsAValidIndexAcrossSeededRolls()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 4, 60, 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            var denseIndex = BodyPartSelection.PickByTypeWithFallback(pool, 0, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Random), mathUtility);

            Assert.IsGreaterThanOrEqualTo(0, denseIndex);
            Assert.AreEqual(0, pool.GetEntityIdByDenseIndex(denseIndex));
        }
    }

    [TestMethod]
    public void FindByPartId_MatchingPartId_ReturnsItsDenseIndex()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, 60, 60, isVital: true));

        var denseIndex = BodyPartSelection.FindByPartId(pool, 0, partId: 1);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void FindByPartId_NoMatchingPartId_ReturnsNegativeOne()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 30, 30, isVital: true));

        var denseIndex = BodyPartSelection.FindByPartId(pool, 0, partId: 5);

        Assert.AreEqual(-1, denseIndex);
    }

    [TestMethod]
    public void PickLowestPercentage_PartCurrentlyBurning_SkippedEvenWithZeroLockout()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, currentHealth: 1, maximumHealth: 10, isVital: false)); // 10%, but burning.
        pool.Add(0, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, currentHealth: 5, maximumHealth: 10, isVital: false)); // 50%, not burning.
        var burningTimers = new MultiComponentPool<BodyPartBurningTimerComponent>(maximumEntityCount: 10, initialCapacity: 4);
        burningTimers.Add(0, new BodyPartBurningTimerComponent(partId: 0, stackCount: 1, framesUntilNextTick: 30));

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0, statModifiers: null, burningTimers);

        Assert.AreEqual("Right Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name, "Left Foot has the lowest fraction but is on fire -- it must be skipped even though its own RegenLockoutFramesRemaining is 0.");
    }

    [TestMethod]
    public void PickLowestPercentage_NoBurningTimersPoolSupplied_BehavesAsBefore()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 0, verticalPosition: 0, currentHealth: 5, maximumHealth: 20, isVital: true));

        var denseIndex = BodyPartSelection.PickLowestPercentage(pool, 0);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickRandom_OneDisabledOneAlive_AlwaysReturnsTheAliveOne()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, 60, 60, isVital: true));
        var torsoDenseIndex = FindDenseIndexByName(pool, 0, "Torso");
        pool.UpdateByDenseIndex(torsoDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            var denseIndex = BodyPartSelection.PickRandom(pool, 0, mathUtility);
            Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
        }
    }

    [TestMethod]
    public void PickRandom_EveryPartDisabled_FallsBackToAnyPart()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 30, 30, isVital: true));
        var headDenseIndex = pool.GetFirstDenseIndex(0);
        pool.UpdateByDenseIndex(headDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);
        var mathUtility = new MathUtility(new Random(1));

        var denseIndex = BodyPartSelection.PickRandom(pool, 0, mathUtility);

        Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByType_MatchingTypeDisabled_AnotherOfSameTypeAlive_ReturnsTheAliveOne()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, 0, 10, isVital: false));
        pool.Add(0, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, 10, 10, isVital: false));
        var leftFootDenseIndex = FindDenseIndexByName(pool, 0, "Left Foot");
        pool.UpdateByDenseIndex(leftFootDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickByType(pool, 0, BodyPartType.Foot);

        Assert.AreEqual("Right Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickByType_OnlyMatchIsDisabled_StillReturnsIt()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, 0, 10, isVital: false));
        var footDenseIndex = pool.GetFirstDenseIndex(0);
        pool.UpdateByDenseIndex(footDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickByType(pool, 0, BodyPartType.Foot);

        Assert.AreEqual("Left Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name, "A disabled-but-only match is still a valid answer -- not -1.");
    }

    [TestMethod]
    public void PickTopmost_HighestPositionDisabled_ReturnsNextHighestAlive()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, 10, 10, isVital: false));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, 60, 60, isVital: true));
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 2, verticalPosition: 5, 0, 30, isVital: true));
        var headDenseIndex = FindDenseIndexByName(pool, 0, "Head");
        pool.UpdateByDenseIndex(headDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickTopmost(pool, 0);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickTopmost_EveryPartDisabled_FallsBackToHighestOverall()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 0, 30, isVital: true));
        var headDenseIndex = pool.GetFirstDenseIndex(0);
        pool.UpdateByDenseIndex(headDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickTopmost(pool, 0);

        Assert.AreEqual("Head", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickBottommost_LowestPositionDisabled_ReturnsNextLowestAlive()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, 30, 30, isVital: true));
        pool.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, 60, 60, isVital: true));
        pool.Add(0, new BodyPartComponent("Foot", BodyPartType.Foot, partId: 2, verticalPosition: 0, 0, 10, isVital: false));
        var footDenseIndex = FindDenseIndexByName(pool, 0, "Foot");
        pool.UpdateByDenseIndex(footDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickBottommost(pool, 0);

        Assert.AreEqual("Torso", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }

    [TestMethod]
    public void PickBottommost_EveryPartDisabled_FallsBackToLowestOverall()
    {
        var pool = CreatePool();
        pool.Add(0, new BodyPartComponent("Foot", BodyPartType.Foot, partId: 0, verticalPosition: 0, 0, 10, isVital: false));
        var footDenseIndex = pool.GetFirstDenseIndex(0);
        pool.UpdateByDenseIndex(footDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        var denseIndex = BodyPartSelection.PickBottommost(pool, 0);

        Assert.AreEqual("Foot", pool.GetReadonlyByDenseIndex(denseIndex).Name);
    }
}
