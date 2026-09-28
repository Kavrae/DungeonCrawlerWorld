using Engine.ECS.Systems;
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
    private const int EntityId = 0;

    private static BodyPartTestWorld CreateWorld(params BodyPartTemplate[] templates)
    {
        var world = new BodyPartTestWorld(templates);
        world.Give(EntityId);
        return world;
    }

    private static string NameOf(BodyPartTestWorld world, int partId)
    {
        Assert.IsTrue(world.BodyParts.TryGet(EntityId, partId, out var part), $"No part {partId}.");
        return part.Name;
    }

    private static BodyPartTemplate Part(string name, BodyPartType type, byte verticalPosition = 0, ushort maximumHealth = 10, bool isVital = false) =>
        new(name, type, verticalPosition, maximumHealth, isVital);

    [TestMethod]
    public void PickRandom_RepeatedSeededRolls_AlwaysLandsOnOneOfTheEntitysParts()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, maximumHealth: 10, isVital: true),
            Part("Torso", BodyPartType.Torso, maximumHealth: 20, isVital: true),
            Part("Arm", BodyPartType.Arm, maximumHealth: 15));
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            var partId = BodyPartSelection.PickRandom(world.BodyParts, EntityId, mathUtility);

            Assert.IsGreaterThanOrEqualTo(0, partId);
            Assert.IsLessThan(3, partId);
        }
    }

    [TestMethod]
    public void PickRandom_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var world = new BodyPartTestWorld();
        var mathUtility = new MathUtility(new Random(1));

        Assert.AreEqual(-1, BodyPartSelection.PickRandom(world.BodyParts, EntityId, mathUtility));
    }

    [TestMethod]
    public void PickLowestPercentage_MixedFractions_PicksLowestFractionPart()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, maximumHealth: 10, isVital: true),
            Part("Torso", BodyPartType.Torso, maximumHealth: 20, isVital: true),
            Part("Arm", BodyPartType.Arm, maximumHealth: 15));
        world.SetHealth(EntityId, 0, 9); // 90%
        world.SetHealth(EntityId, 1, 5); // 25%
        world.SetHealth(EntityId, 2, 10); // ~67%

        var partId = TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0);

        Assert.AreEqual("Torso", NameOf(world, partId));
    }

    [TestMethod]
    public void PickLowestPercentage_LowestPartLockedOut_SkipsItForNextLowest()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, maximumHealth: 10, isVital: true),
            Part("Arm", BodyPartType.Arm, maximumHealth: 15),
            Part("Torso", BodyPartType.Torso, maximumHealth: 20, isVital: true));
        world.SetHealth(EntityId, 0, 9); // 90%
        world.SetHealth(EntityId, 1, 10); // ~67%
        world.SetHealth(EntityId, 2, 5); // 25%, locked out below.
        world.BodyParts.LockOutOfRegen(EntityId, 2, now: 0, lockoutFrames: 100);

        var partId = TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0);

        Assert.AreEqual("Arm", NameOf(world, partId));
    }

    [TestMethod]
    public void PickLowestPercentage_EveryPartFullOrLockedOut_ReturnsNegativeOne()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, maximumHealth: 10, isVital: true), // Full.
            Part("Torso", BodyPartType.Torso, maximumHealth: 20, isVital: true)); // Damaged but locked out.
        world.SetHealth(EntityId, 1, 5);
        world.BodyParts.LockOutOfRegen(EntityId, 1, now: 0, lockoutFrames: 50);

        Assert.AreEqual(-1, TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0));
    }

    [TestMethod]
    public void PickLowestPercentage_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var world = new BodyPartTestWorld();

        Assert.AreEqual(-1, TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0));
    }

    [TestMethod]
    public void PickLowestPercentage_PartAtRawMaximumWithActiveBuff_StillSelectableUpToTheEffectiveMaximum()
    {
        // At its raw maximum (100% by that measure), but a +50% MaximumHealth buff means its real
        // cap is 60 -- this part still has headroom and must not be treated as "already full."
        var world = CreateWorld(Part("Head", BodyPartType.Head, maximumHealth: 40, isVital: true));
        var statModifiers = BuffedMaximumHealth();

        var partId = TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0, statModifiers);

        Assert.AreEqual("Head", NameOf(world, partId));
    }

    [TestMethod]
    public void PickLowestPercentage_PartAtItsEffectiveMaximumWithActiveBuff_NotSelected()
    {
        // 60/60 with the same +50% buff active (effective maximum is 60) -- genuinely full, unlike the case above.
        var world = CreateWorld(Part("Head", BodyPartType.Head, maximumHealth: 40, isVital: true));
        world.BodyParts.SetCurrentHealth(EntityId, 0, 60);
        var statModifiers = BuffedMaximumHealth();

        Assert.AreEqual(-1, TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 0, statModifiers));
    }

    private static MultiComponentPool<StatModifierComponent> BuffedMaximumHealth()
    {
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        return statModifiers;
    }

    [TestMethod]
    public void PickTopmost_MixedVerticalPositions_PicksHighestPosition()
    {
        var world = CreateWorld(
            Part("Foot", BodyPartType.Foot, verticalPosition: 0),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true),
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true));

        Assert.AreEqual("Head", NameOf(world, BodyPartSelection.PickTopmost(world.BodyParts, EntityId)));
    }

    [TestMethod]
    public void PickTopmost_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var world = new BodyPartTestWorld();

        Assert.AreEqual(-1, BodyPartSelection.PickTopmost(world.BodyParts, EntityId));
    }

    [TestMethod]
    public void PickBottommost_MixedVerticalPositions_PicksLowestPosition()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true),
            Part("Foot", BodyPartType.Foot, verticalPosition: 0));

        Assert.AreEqual("Foot", NameOf(world, BodyPartSelection.PickBottommost(world.BodyParts, EntityId)));
    }

    [TestMethod]
    public void PickBottommost_EntityWithNoBodyParts_ReturnsNegativeOne()
    {
        var world = new BodyPartTestWorld();

        Assert.AreEqual(-1, BodyPartSelection.PickBottommost(world.BodyParts, EntityId));
    }

    [TestMethod]
    public void PickByType_MatchingTypePresent_ReturnsItsPartId()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, maximumHealth: 30, isVital: true),
            Part("Left Foot", BodyPartType.Foot));

        Assert.AreEqual("Left Foot", NameOf(world, BodyPartSelection.PickByType(world.BodyParts, EntityId, BodyPartType.Foot)));
    }

    [TestMethod]
    public void PickByType_NoMatchingType_ReturnsNegativeOne()
    {
        var world = CreateWorld(Part("Head", BodyPartType.Head, maximumHealth: 30, isVital: true));

        Assert.AreEqual(-1, BodyPartSelection.PickByType(world.BodyParts, EntityId, BodyPartType.Foot));
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypePresent_IgnoresFallback()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Left Foot", BodyPartType.Foot));
        var mathUtility = new MathUtility(new Random(1));

        var partId = BodyPartSelection.PickByTypeWithFallback(world.BodyParts, EntityId, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Topmost), mathUtility);

        Assert.AreEqual("Left Foot", NameOf(world, partId));
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_FallsBackToTopmost()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        var partId = BodyPartSelection.PickByTypeWithFallback(world.BodyParts, EntityId, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Topmost), mathUtility);

        Assert.AreEqual("Head", NameOf(world, partId));
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_FallsBackToBottommost()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        var partId = BodyPartSelection.PickByTypeWithFallback(world.BodyParts, EntityId, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Bottommost), mathUtility);

        Assert.AreEqual("Torso", NameOf(world, partId));
    }

    [TestMethod]
    public void PickByTypeWithFallback_PreferredTypeAbsent_RandomFallback_AlwaysReturnsAValidPartAcrossSeededRolls()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true));
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            var partId = BodyPartSelection.PickByTypeWithFallback(world.BodyParts, EntityId, new BodyPartTargetRule(BodyPartType.Foot, BodyPartFallback.Random), mathUtility);

            Assert.IsGreaterThanOrEqualTo(0, partId);
            Assert.IsLessThan(2, partId);
        }
    }

    [TestMethod]
    public void TryGet_MatchingPartId_ReturnsThatPart_AndFalseBeyondTheBodyPlan()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true));

        Assert.IsTrue(world.BodyParts.TryGet(EntityId, 1, out var torso));
        Assert.AreEqual("Torso", torso.Name);
        Assert.IsFalse(world.BodyParts.TryGet(EntityId, 5, out _));
    }

    [TestMethod]
    public void PickRandom_OneDisabledOneAlive_AlwaysReturnsTheAliveOne()
    {
        var world = CreateWorld(
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true));
        world.SetHealth(EntityId, 1, 0);
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 50; i++)
        {
            Assert.AreEqual("Head", NameOf(world, BodyPartSelection.PickRandom(world.BodyParts, EntityId, mathUtility)));
        }
    }

    [TestMethod]
    public void PickRandom_EveryPartDisabled_FallsBackToAnyPart()
    {
        var world = CreateWorld(Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true));
        world.SetHealth(EntityId, 0, 0);
        var mathUtility = new MathUtility(new Random(1));

        Assert.AreEqual("Head", NameOf(world, BodyPartSelection.PickRandom(world.BodyParts, EntityId, mathUtility)));
    }

    [TestMethod]
    public void PickByType_MatchingTypeDisabled_AnotherOfSameTypeAlive_ReturnsTheAliveOne()
    {
        var world = CreateWorld(
            Part("Left Foot", BodyPartType.Foot),
            Part("Right Foot", BodyPartType.Foot));
        world.SetHealth(EntityId, 0, 0);

        Assert.AreEqual("Right Foot", NameOf(world, BodyPartSelection.PickByType(world.BodyParts, EntityId, BodyPartType.Foot)));
    }

    [TestMethod]
    public void PickByType_OnlyMatchIsDisabled_StillReturnsIt()
    {
        var world = CreateWorld(Part("Left Foot", BodyPartType.Foot));
        world.SetHealth(EntityId, 0, 0);

        Assert.AreEqual("Left Foot", NameOf(world, BodyPartSelection.PickByType(world.BodyParts, EntityId, BodyPartType.Foot)), "A disabled-but-only match is still a valid answer -- not -1.");
    }

    [TestMethod]
    public void PickTopmost_HighestPositionDisabled_ReturnsNextHighestAlive()
    {
        var world = CreateWorld(
            Part("Foot", BodyPartType.Foot, verticalPosition: 0),
            Part("Torso", BodyPartType.Torso, verticalPosition: 4, maximumHealth: 60, isVital: true),
            Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true));
        world.SetHealth(EntityId, 2, 0);

        Assert.AreEqual("Torso", NameOf(world, BodyPartSelection.PickTopmost(world.BodyParts, EntityId)));
    }

    [TestMethod]
    public void PickTopmost_EveryPartDisabled_FallsBackToHighestOverall()
    {
        var world = CreateWorld(Part("Head", BodyPartType.Head, verticalPosition: 5, maximumHealth: 30, isVital: true));
        world.SetHealth(EntityId, 0, 0);

        Assert.AreEqual("Head", NameOf(world, BodyPartSelection.PickTopmost(world.BodyParts, EntityId)));
    }
}
