using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Burning;

/// <summary>BurningApplier holds a burn where it is told to: on the named body part, or on the entity as a whole when no part is named.</summary>
[TestClass]
public sealed class BurningApplierTests
{
    private const int EntityId = 0;
    private const byte HeadPartId = 0;
    private const byte FootPartId = 1;

    private static ComponentManager CreateComponentManager() =>
        BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));

    private static BodyPartTestWorld AddComplexBodyParts(ComponentManager componentManager) =>
        BodyPartTestWorld.WithParts(componentManager, EntityId, ("Head", BodyPartType.Head, 30, 30, true), ("Left Foot", BodyPartType.Foot, 10, 10, false));

    private static BurningApplier CreateApplier(ComponentManager componentManager, BodyPartTestWorld world) =>
        new(componentManager, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

    private static int PartStacks(ComponentManager componentManager, byte partId)
    {
        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        for (var denseIndex = bodyPartTimers.GetFirstDenseIndex(EntityId); denseIndex != -1; denseIndex = bodyPartTimers.GetNextDenseIndex(denseIndex))
        {
            var timer = bodyPartTimers.GetReadonlyByDenseIndex(denseIndex);
            if (timer.PartId == partId)
            {
                return timer.StackCount;
            }
        }

        return 0;
    }

    [TestMethod]
    public void ApplyStacks_PartNamed_HoldsTheBurnOnThatPartOnly()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        var landed = applier.ApplyStacks(EntityId, count: 3, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.AreEqual(3, landed);
        Assert.AreEqual(3, PartStacks(componentManager, FootPartId));
        Assert.AreEqual(0, PartStacks(componentManager, HeadPartId));
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId), "A burn held on a part is not also held on the entity.");
    }

    [TestMethod]
    public void ApplyStacks_NoPartNamed_HoldsTheBurnOnTheEntity()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        applier.ApplyStacks(EntityId, count: 2, ActionSource.Admin, now: 0);

        Assert.AreEqual(2, componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(EntityId).StackCount);
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    /// <summary>Where the entity stands plays no part: standing on terrain with a contact, a grant that names no part is still the entity's.</summary>
    [TestMethod]
    public void ApplyStacks_NoPartNamedWhileExposedToAContact_StillHoldsTheBurnOnTheEntity()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));
        componentManager.GetPackedPool<Game.Modules.TerrainContacts.Components.TerrainContactExposureComponent>().Add(EntityId, new(nextTickFrame: 60, terrainTypeId: 1));

        applier.ApplyStacks(EntityId, count: 2, ActionSource.Admin, now: 0);

        Assert.AreEqual(2, componentManager.GetPackedPool<BurningTimerComponent>().GetReadonly(EntityId).StackCount);
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStacks_PartNamedForAnEntityWithoutBodyParts_HoldsTheBurnOnTheEntity()
    {
        var componentManager = CreateComponentManager();
        var world = new BodyPartTestWorld(componentManager);
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(EntityId, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 50));
        var applier = CreateApplier(componentManager, world);

        applier.ApplyStacks(EntityId, count: 1, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.IsTrue(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
        Assert.AreEqual(1, applier.GetCurrentStackCount(EntityId, FootPartId));
    }

    [TestMethod]
    public void ApplyStacks_SamePartAgain_TopsOffItsOneTimer()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        applier.ApplyStacks(EntityId, count: 1, ActionSource.Admin, now: 0, bodyPartId: FootPartId);
        applier.ApplyStacks(EntityId, count: 2, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.AreEqual(1, componentManager.GetMultiPool<BodyPartBurningTimerComponent>().CountForEntity(EntityId));
        Assert.AreEqual(3, PartStacks(componentManager, FootPartId));
        Assert.AreEqual(3, applier.GetCurrentStackCount(EntityId, FootPartId));
    }

    /// <summary>Each part burns on its own, and beside an entity-wide burn: none of the three counts sees another's stacks.</summary>
    [TestMethod]
    public void ApplyStacks_DifferentPartsAndTheEntity_AreCountedSeparately()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        applier.ApplyStacks(EntityId, count: 4, ActionSource.Admin, now: 0, bodyPartId: FootPartId);
        applier.ApplyStacks(EntityId, count: 2, ActionSource.Admin, now: 0, bodyPartId: HeadPartId);
        applier.ApplyStacks(EntityId, count: 1, ActionSource.Admin, now: 0);

        Assert.AreEqual(4, applier.GetCurrentStackCount(EntityId, FootPartId));
        Assert.AreEqual(2, applier.GetCurrentStackCount(EntityId, HeadPartId));
        Assert.AreEqual(1, applier.GetCurrentStackCount(EntityId));
        Assert.AreEqual(2, componentManager.GetMultiPool<BodyPartBurningTimerComponent>().CountForEntity(EntityId));
    }

    [TestMethod]
    public void ApplyStacks_MoreThanAPartCanHold_LandsOnlyUpToTheCap()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        var first = applier.ApplyStacks(EntityId, count: 15, ActionSource.Admin, now: 0, bodyPartId: FootPartId);
        var second = applier.ApplyStacks(EntityId, count: 15, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.AreEqual(15, first);
        Assert.AreEqual(BurningEffects.MaxStacks - 15, second);
        Assert.AreEqual(BurningEffects.MaxStacks, PartStacks(componentManager, FootPartId));
    }

    [TestMethod]
    public void ApplyStacks_ImmuneEntity_LandsNothingOnThePart()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));
        StatusEffectImmunityEffects.GrantPermanent(componentManager.GetMultiPool<StatusEffectImmunityComponent>(), EntityId, StatusEffectType.Burning);

        var landed = applier.ApplyStacks(EntityId, count: 3, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.AreEqual(0, landed);
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStacks_IgnitingAPart_LocksItOutOfRegenBeforeItsFirstTick()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        world.SetHealth(EntityId, FootPartId, 5);
        var applier = CreateApplier(componentManager, world);

        applier.ApplyStacks(EntityId, count: 1, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.AreEqual(-1, TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 1));
    }

    [TestMethod]
    public void RemoveAllStacks_PutsOutTheEntityAndEveryPart()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));
        applier.ApplyStacks(EntityId, count: 4, ActionSource.Admin, now: 0, bodyPartId: FootPartId);
        applier.ApplyStacks(EntityId, count: 2, ActionSource.Admin, now: 0, bodyPartId: HeadPartId);
        applier.ApplyStacks(EntityId, count: 1, ActionSource.Admin, now: 0);

        Assert.IsTrue(applier.RemoveAllStacks(EntityId));

        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void RemoveAllStacks_BurnHeldOnlyOnABodyPart_RemovesItAndSaysSo()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));
        applier.ApplyStacks(EntityId, count: 4, ActionSource.Admin, now: 0, bodyPartId: FootPartId);

        Assert.IsTrue(applier.RemoveAllStacks(EntityId));

        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void RemoveAllStacks_NothingBurning_SaysThereWasNothingToRemove()
    {
        var componentManager = CreateComponentManager();
        var applier = CreateApplier(componentManager, AddComplexBodyParts(componentManager));

        Assert.IsFalse(applier.RemoveAllStacks(EntityId));
    }
}
