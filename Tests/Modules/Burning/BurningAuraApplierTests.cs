using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.ContactDamage.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Burning;

/// <summary>
/// Drives BurningAuraApplier.ApplyStack/GetCurrentStackCount the same way
/// StatusEffectAuraSystem.GrantStacks does (source always StatusEffectSource.Admin -- see
/// BurningAuraApplier's own doc comment for why "source traces to a hazard" alone can't be the
/// real signal, and hazard exposure is read from the target's own ContactDamageExposureComponent
/// instead).
/// </summary>
[TestClass]
public sealed class BurningAuraApplierTests
{
    private const int EntityId = 0;
    private const int HazardEntityId = 99;

    private static ComponentManager CreateComponentManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterMultiPool<BodyPartComponent>();
        componentManager.RegisterMultiPool<BodyPartBurningTimerComponent>();
        componentManager.RegisterMultiPool<BodyPartStatusEffectStack>();
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<ContactDamageExposureComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<DamageOnContactComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    private static void AddComplexBodyParts(ComponentManager componentManager)
    {
        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        bodyParts.Add(EntityId, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, currentHealth: 30, maximumHealth: 30, isVital: true));
        bodyParts.Add(EntityId, new BodyPartComponent("Left Foot", BodyPartType.Foot, partId: 1, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
    }

    [TestMethod]
    public void ApplyStack_HazardExposedComplexTarget_GrantsBodyPartScopedBurnOnBottommostPart()
    {
        var componentManager = CreateComponentManager();
        AddComplexBodyParts(componentManager);
        componentManager.GetPackedPool<DamageOnContactComponent>().Add(HazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(framesUntilNextTick: 60, sourceEntityId: HazardEntityId));
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        Assert.IsTrue(bodyPartTimers.Has(EntityId));
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId, "No PreferredTargetType on the hazard -- Bottommost fallback selects the Foot (PartId 1), not the Head.");
        Assert.IsFalse(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId), "A hazard-exposed Complex target must not also get the entity-scoped timer.");
    }

    [TestMethod]
    public void ApplyStack_NoHazardExposure_GrantsEntityScopedBurnUnchanged()
    {
        var componentManager = CreateComponentManager();
        AddComplexBodyParts(componentManager);
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

        Assert.IsTrue(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStack_HazardExposedSimpleTarget_GrantsEntityScopedBurn()
    {
        var componentManager = CreateComponentManager();
        // No BodyPartComponent at all for EntityId -- Simple, regardless of hazard exposure.
        componentManager.GetPackedPool<DamageOnContactComponent>().Add(HazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(framesUntilNextTick: 60, sourceEntityId: HazardEntityId));
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

        Assert.IsTrue(componentManager.GetPackedPool<BurningTimerComponent>().Has(EntityId));
        Assert.IsFalse(componentManager.GetMultiPool<BodyPartBurningTimerComponent>().Has(EntityId));
    }

    [TestMethod]
    public void ApplyStack_HazardExposedTarget_RepeatedCalls_TopsOffSameParts_StackCountMatchesGetCurrentStackCount()
    {
        var componentManager = CreateComponentManager();
        AddComplexBodyParts(componentManager);
        componentManager.GetPackedPool<DamageOnContactComponent>().Add(HazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(framesUntilNextTick: 60, sourceEntityId: HazardEntityId));
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);
        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);
        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

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
        AddComplexBodyParts(componentManager);
        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        bodyParts.Add(EntityId, new BodyPartComponent("Right Foot", BodyPartType.Foot, partId: 2, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: false));
        componentManager.GetPackedPool<DamageOnContactComponent>().Add(HazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        componentManager.GetPackedPool<ContactDamageExposureComponent>().Add(EntityId, new ContactDamageExposureComponent(framesUntilNextTick: 60, sourceEntityId: HazardEntityId));
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        var originalPartId = bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId;
        Assert.IsTrue(originalPartId is 1 or 2, "Bottommost fallback with a tie between two Feet resolves to whichever Foot iterates first (PartId 1 or 2).");

        var burningPartDenseIndex = BodyPartSelection.FindByPartId(bodyParts, EntityId, originalPartId);
        bodyParts.UpdateByDenseIndex(burningPartDenseIndex, static (ref BodyPartComponent part) => part.IsDisabled = true);

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

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
        AddComplexBodyParts(componentManager);
        var hazards = componentManager.GetPackedPool<DamageOnContactComponent>();
        var exposures = componentManager.GetPackedPool<ContactDamageExposureComponent>();
        const int headHazardEntityId = HazardEntityId + 1;
        hazards.Add(HazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        hazards.Add(headHazardEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60, preferredTargetType: BodyPartType.Head));
        exposures.Add(EntityId, new ContactDamageExposureComponent(framesUntilNextTick: 60, sourceEntityId: HazardEntityId));
        var applier = new BurningAuraApplier(new MathUtility());

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

        var bodyPartTimers = componentManager.GetMultiPool<BodyPartBurningTimerComponent>();
        Assert.AreEqual(1, bodyPartTimers.CountForEntity(EntityId));
        Assert.AreEqual((byte)1, bodyPartTimers.GetReadonlyByDenseIndex(bodyPartTimers.GetFirstDenseIndex(EntityId)).PartId, "The generic hazard's Bottommost fallback burns the Foot (PartId 1) first.");

        // Entity now steps onto a different hazard tile (its own PreferredTargetType of Head), while the Foot burn hasn't decayed yet.
        exposures.TryUpdate(EntityId, headHazardEntityId, static (ref ContactDamageExposureComponent exposure, int sourceEntityId) => exposure.SourceEntityId = sourceEntityId);

        applier.ApplyStack(componentManager, EntityId, StatusEffectSource.Admin);

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
