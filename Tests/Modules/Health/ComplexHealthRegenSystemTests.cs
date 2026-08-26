using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.Health;

/// <summary>
/// Every test here constructs ComplexHealthRegenSystem against an empty bodyParts pool, then
/// Adds body parts afterward -- mirroring real bootstrap ordering (HealthModule.RegisterSystems
/// runs before FloorBuilder.PopulateFloor grants any BodyPartComponent). Adding several parts to
/// the same entity before construction would double (or N-times) count that entity when
/// ProcessingTierWiring.CreateAndWire seeds its TieredEntityStripeSet from the driving pool's raw
/// EntityIds span -- one entry per component instance, not deduplicated per entity, unlike the
/// EntityAdded event's own "0-to-1 transition only" firing that construction-after-Add relies on
/// instead.
/// </summary>
[TestClass]
public sealed class ComplexHealthRegenSystemTests
{
    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);

    /// <summary>Constitution total 300 -- ComplexHealthRegenSystem's MaxHealthRegenPerSecond, a flat 6 HP/sec -- so a Local-tier visit (StripeCount is a full second's worth of frames) regens a clean 6.</summary>
    private static MultiComponentPool<AbilityScoreComponent> CreateAbilityScoresPoolWithMaxConstitution(int entityId)
    {
        var pool = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(entityId, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
        return pool;
    }

    private static BodyPartComponent GetPart(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, string name)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.Name == name)
            {
                return part;
            }
        }

        throw new InvalidOperationException($"No part named {name} for entity {entityId}.");
    }

    private static void SetLockout(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, string name, ushort framesRemaining)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (bodyParts.GetReadonlyByDenseIndex(denseIndex).Name == name)
            {
                bodyParts.UpdateByDenseIndex(denseIndex, framesRemaining, static (ref BodyPartComponent part, ushort frames) => part.RegenLockoutFramesRemaining = frames);
                return;
            }
        }
    }

    [TestMethod]
    public void Update_RegeneratesSinglePartByLiveComputedConstitutionAmount()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(56f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumHealth()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 199, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(200f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotRegenerate()
    {
        var bodyParts = CreateBodyPartsPool();
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities, abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 0, maximumHealth: 200, isVital: true));
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        system.Update(default, 0);

        Assert.AreEqual(0f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_NoAbilityScorePool_LeavesCurrentHealthUnchanged()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents());
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_NoConstitutionScoreForEntity_LeavesCurrentHealthUnchanged()
    {
        var bodyParts = CreateBodyPartsPool();
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_SelectsLowestPercentageEligiblePart()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, currentHealth: 10, maximumHealth: 100, isVital: true)); // 10%
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 90, maximumHealth: 100, isVital: true)); // 90%

        system.Update(default, 0);

        Assert.AreEqual(16f, GetPart(bodyParts, 0, "Head").CurrentHealth, "The lowest-percentage part must be selected for this visit's regen.");
        Assert.AreEqual(90f, GetPart(bodyParts, 0, "Torso").CurrentHealth, "An unselected part must not also regen this same visit.");
    }

    [TestMethod]
    public void Update_LockedOutPart_CountdownStillDecrementsEvenWhenNotSelected()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents());
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, currentHealth: 5, maximumHealth: 100, isVital: true)); // Lowest %, but locked out.
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 50, maximumHealth: 100, isVital: true));
        SetLockout(bodyParts, 0, "Head", 100);

        system.Update(default, 0);

        Assert.AreEqual(40, GetPart(bodyParts, 0, "Head").RegenLockoutFramesRemaining, "A locked-out part's own countdown must still decrement by this visit's framesPerVisit even though it wasn't selected for healing.");
        Assert.AreEqual(5f, GetPart(bodyParts, 0, "Head").CurrentHealth, "No AbilityScoresModule wired -- neither part should have actually regenerated.");
        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_PartExitsLockoutAfterEnoughTicks_BecomesSelectableAgain()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 50, maximumHealth: 200, isVital: true));
        SetLockout(bodyParts, 0, "Torso", 120);

        system.Update(default, 0); // Lockout 120 -> 60 (still > 0): skipped, no regen this visit.

        Assert.AreEqual(60, GetPart(bodyParts, 0, "Torso").RegenLockoutFramesRemaining);
        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);

        system.Update(default, 0); // Lockout 60 -> 0: eligible again, regens this same visit.

        Assert.AreEqual(0, GetPart(bodyParts, 0, "Torso").RegenLockoutFramesRemaining);
        Assert.AreEqual(56f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_PartHealedAboveZero_ClearsIsDisabled()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, currentHealth: 0, maximumHealth: 200, isVital: true));
        bodyParts.UpdateByDenseIndex(bodyParts.GetFirstDenseIndex(0), static (ref BodyPartComponent part) => part.IsDisabled = true);

        system.Update(default, 0);

        Assert.IsFalse(GetPart(bodyParts, 0, "Torso").IsDisabled);
    }
}
