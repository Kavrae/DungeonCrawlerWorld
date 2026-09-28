using Engine.Events;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Terrain;
using Game.World;

namespace Tests.Modules.Burning;

/// <summary>
/// Drives BurningAuraApplier.ApplyStack/GetCurrentStackCount the same way
/// StatusEffectAuraSystem.GrantStacks does (source always ActionSource.Admin -- see
/// BurningAuraApplier's own doc comment for why "source traces to a hazard" alone can't be the
/// real signal, and hazard exposure is read from the target's own ContactDamageExposureComponent
/// instead).
/// </summary>
[TestClass]
public sealed class BurningAuraApplierTests
{
    private const int EntityId = 0;

    /// <summary>A registry holding one hazard with no preferred part and one that prefers the head, plus their ids.</summary>
    private static (TerrainRegistry Terrain, ushort Hazard, ushort HeadHazard) CreateTerrain()
    {
        var terrain = new TerrainRegistry();
        var hazard = terrain.Register(new TerrainDefinition("test:hazard", "Hazard", "", default, "~", default, ContactHazard: new ContactHazard(DamagePerTick: 10, TickIntervalFrames: 60)));
        var headHazard = terrain.Register(new TerrainDefinition("test:head-hazard", "Head hazard", "", default, "^", default, ContactHazard: new ContactHazard(DamagePerTick: 10, TickIntervalFrames: 60, PreferredTargetType: BodyPartType.Head)));
        return (terrain, hazard, headHazard);
    }

    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        return componentManager;
    }

    private static BodyPartTestWorld AddComplexBodyParts(ComponentManager componentManager) =>
        BodyPartTestWorld.WithParts(componentManager, EntityId, ("Head", BodyPartType.Head, 30, 30, true), ("Left Foot", BodyPartType.Foot, 10, 10, false));

    /// <summary>A Head and two Feet, so the bottommost fallback has a tie between paired parts to settle the way a real race does.</summary>
    private static BodyPartTestWorld AddComplexBodyPartsWithPairedFeet(ComponentManager componentManager) =>
        BodyPartTestWorld.WithParts(componentManager, EntityId, ("Head", BodyPartType.Head, 30, 30, true), ("Left Foot", BodyPartType.Foot, 10, 10, false), ("Right Foot", BodyPartType.Foot, 10, 10, false));

    [TestMethod]
    public void ApplyStack_HazardExposedComplexTarget_GrantsBodyPartScopedBurnOnBottommostPart()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        var (terrain, hazard, _) = CreateTerrain();
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        Assert.IsTrue(bodyPartTimers.Has(EntityId));
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId, "No PreferredTargetType on the hazard -- Bottommost fallback selects the Foot (PartId 1), not the Head.");
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId), "A hazard-exposed Complex target must not also get the entity-scoped timer.");
    }

    [TestMethod]
    public void ApplyStack_IgnitingAPart_LocksItOutOfRegenBeforeItsFirstTick()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        world.SetHealth(EntityId, 1, 5);
        var (terrain, hazard, _) = CreateTerrain();
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        Assert.AreEqual(-1, TestHealth.PickLowestPercentage(world.BodyParts, EntityId, now: 1));
    }

    [TestMethod]
    public void ApplyStack_NoHazardExposure_GrantsEntityScopedBurnUnchanged()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        var applier = new BurningAuraApplier(new MathUtility(), CreateTerrain().Terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        Assert.IsTrue(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStack_HazardExposedSimpleTarget_GrantsEntityScopedBurn()
    {
        var componentManager = CreateComponentManager();
        // No body plan at all for EntityId -- Simple, regardless of hazard exposure.
        var world = new BodyPartTestWorld(componentManager);
        var (terrain, hazard, _) = CreateTerrain();
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        Assert.IsTrue(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStack_HazardExposedTarget_RepeatedCalls_TopsOffSameParts_StackCountMatchesGetCurrentStackCount()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        var (terrain, hazard, _) = CreateTerrain();
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);
        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);
        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        Assert.AreEqual(1, bodyPartTimers.CountForEntity(EntityId), "All three stacks land on the same, single resolved part -- one timer entry, not three.");
        Assert.AreEqual((byte)3, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).StackCount);
        Assert.AreEqual(3, applier.GetCurrentStackCount(componentManager, EntityId), "GetCurrentStackCount must resolve to the same part and report its real stack count -- StatusEffectAuraSystem.GrantStacks relies on this for its own top-off math.");
    }

    /// <summary>
    /// Regression test for the bug where PickBottommost/PickByType's own "prefer a non-disabled
    /// part" fallback (BodyPartSelection) caused ResolveTargetPartId to silently retarget a
    /// *different* Foot once the originally-burning one hit 0 and became disabled -- spreading
    /// the fire to an untouched part instead of continuing to top off the one already burning.
    /// </summary>
    [TestMethod]
    public void ApplyStack_OriginalTargetPartDisabledMidBurn_KeepsToppingOffSamePart()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyPartsWithPairedFeet(componentManager);
        var bodyParts = world.BodyParts;
        var (terrain, hazard, _) = CreateTerrain();
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        var originalPartId = bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId;
        Assert.IsTrue(originalPartId is 1 or 2, "Bottommost fallback with a tie between two Feet resolves to whichever Foot iterates first (PartId 1 or 2).");

        bodyParts.SetCurrentHealth(EntityId, originalPartId, 0f);
        bodyParts.Damage(EntityId, originalPartId, amount: 1f, effectiveMaximumHealth: 10f, now: 0, lockoutFrames: 0);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        Assert.AreEqual(1, bodyPartTimers.CountForEntity(EntityId), "Must keep topping off the same, already-burning part, not spread a second burn to the other Foot.");
        Assert.AreEqual(originalPartId, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId);
        Assert.AreEqual((byte)2, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).StackCount);
    }

    /// <summary>
    /// A different hazard (its own PreferredTargetType) exposing entityId while an earlier hazard's
    /// burn on a different part hasn't decayed yet must ignite its own part independently, not fold
    /// into the already-burning one -- proving the stickiness fix above (reusing the deterministic,
    /// disabled-status-independent PickByTypeWithFallback(preferAlive: false) resolution) is scoped
    /// to "the same rule resolves to the same part," not "any existing burn is fair game to reuse."
    /// </summary>
    [TestMethod]
    public void ApplyStack_DifferentHazardPreferredType_WhileAnotherPartAlreadyBurning_TargetsItsOwnPart()
    {
        var componentManager = CreateComponentManager();
        var world = AddComplexBodyParts(componentManager);
        var exposures = componentManager.GetPackedPool<ContactDamageExposureComponent>();
        var (terrain, hazard, headHazard) = CreateTerrain();
        exposures.Add(EntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardTerrainTypeId: hazard));
        var applier = new BurningAuraApplier(new MathUtility(), terrain, world.Definitions, new EventBus(), TestPlayerQuery.NoPlayer);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        Assert.AreEqual(1, bodyPartTimers.CountForEntity(EntityId));
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId, "The generic hazard's Bottommost fallback burns the Foot (PartId 1) first.");

        // Entity now steps onto a different hazard tile (its own PreferredTargetType of Head), while the Foot burn hasn't decayed yet.
        exposures.TryUpdate(EntityId, headHazard, static (ref ContactDamageExposureComponent exposure, ushort hazardTerrainTypeId) => exposure.HazardTerrainTypeId = hazardTerrainTypeId);

        applier.ApplyStack(componentManager, EntityId, ActionSource.Admin, now: 0);

        Assert.AreEqual(2, bodyPartTimers.CountForEntity(EntityId), "The Head-preferring hazard must ignite its own part, not fold into the already-burning Foot.");
        var footTimerDenseIndex = FindTimerByPartId(bodyPartTimers, EntityId, partId: 1);
        var headTimerDenseIndex = FindTimerByPartId(bodyPartTimers, EntityId, partId: 0);
        Assert.AreNotEqual(-1, footTimerDenseIndex, "The original Foot burn must still be present, untouched.");
        Assert.AreNotEqual(-1, headTimerDenseIndex, "The new Head burn must exist as its own, separate timer entry.");
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(footTimerDenseIndex).StackCount, "Foot's own stack count is untouched by the Head grant.");
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(headTimerDenseIndex).StackCount);
    }

    private static int FindTimerByPartId(MultiComponentPool<BodyPartBurningTimerComponent> timers, int entityId, byte partId)
    {
        for (var denseIndex = timers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = timers.GetNextDenseIndex(denseIndex))
        {
            if (timers.GetReadonlyByDenseIndex(denseIndex).PartId == partId)
            {
                return denseIndex;
            }
        }

        return -1;
    }
}
