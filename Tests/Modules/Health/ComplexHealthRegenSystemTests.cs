using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

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

    /// <summary>StripeCount is a full second of frames and entity 0 sits in bucket 0, so any frame that is a multiple of it is a due visit -- 0 and 120 both are.</summary>
    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    /// <summary>Always empty -- ComplexHealthRegenSystem only needs this to satisfy HealthHeal.Apply's Simple-vs-Complex dispatch check, which always resolves to the Complex branch for the body-parts-only entities this system drives.</summary>
    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Seeds entity 0's tier explicitly -- see SimpleHealthRegenSystemTests.CreateTiersPool's own note on why leaving it absent silently tested the Beyond cadence.</summary>
    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool(ProcessingTierLevel tier = ProcessingTierLevel.Local)
    {
        var pool = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        pool.Add(0, new ProcessingTierComponent(tier));
        return pool;
    }

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

    private static void SetLockout(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, string name, uint lockedUntilFrame)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (bodyParts.GetReadonlyByDenseIndex(denseIndex).Name == name)
            {
                bodyParts.UpdateByDenseIndex(denseIndex, lockedUntilFrame, static (ref BodyPartComponent part, uint frames) => part.RegenLockedUntilFrame = frames);
                return;
            }
        }
    }

    [TestMethod]
    public void Update_RegeneratesSinglePartByLiveComputedConstitutionAmount()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(56f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    /// <summary>Regen is now routed through HealthHeal.Apply (sourceEntityId: entityId, a self-heal) -- an IncomingHealing modifier scales the regen tick the same way it would scale a potion or spell.</summary>
    [TestMethod]
    public void Update_IncomingHealingModifier_ScalesRegenTick()
    {
        var bodyParts = CreateBodyPartsPool();
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingHealing, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.Admin));
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers, abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(59f, GetPart(bodyParts, 0, "Torso").CurrentHealth, "6 base regen * 1.5 = 9; 50 + 9 = 59.");
    }

    [TestMethod]
    public void Update_ClampsAtMaximumHealth()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 199, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(200f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotRegenerate()
    {
        var bodyParts = CreateBodyPartsPool();
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities, abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 0, maximumHealth: 200, isVital: true));
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        system.Update(default, 0);

        Assert.AreEqual(0f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_NoAbilityScorePool_LeavesCurrentHealthUnchanged()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents());
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_NoConstitutionScoreForEntity_LeavesCurrentHealthUnchanged()
    {
        var bodyParts = CreateBodyPartsPool();
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 200, isVital: true));

        system.Update(default, 0);

        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_SelectsLowestPercentageEligiblePart()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 10, maximumHealth: 100, isVital: true)); // 10%
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 90, maximumHealth: 100, isVital: true)); // 90%

        system.Update(default, 0);

        Assert.AreEqual(16f, GetPart(bodyParts, 0, "Head").CurrentHealth, "The lowest-percentage part must be selected for this visit's regen.");
        Assert.AreEqual(90f, GetPart(bodyParts, 0, "Torso").CurrentHealth, "An unselected part must not also regen this same visit.");
    }

    /// <summary>Nothing advances the lockout any more -- it is a deadline (BodyPartComponent.RegenLockedUntilFrame), so a visit while it is still running leaves the part exactly as it found it.</summary>
    [TestMethod]
    public void Update_LockedOutPart_IsSkippedAndLeftUntouched()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, currentHealth: 5, maximumHealth: 100, isVital: true)); // Lowest %, but locked out.
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 100, isVital: true));
        SetLockout(bodyParts, 0, "Head", 100);

        system.Update(Frame(0), 0);

        Assert.AreEqual(100u, GetPart(bodyParts, 0, "Head").RegenLockedUntilFrame, "The lockout is a deadline -- this visit must not change it.");
        Assert.AreEqual(5f, GetPart(bodyParts, 0, "Head").CurrentHealth, "Locked out, so it must not be the part that regenerates.");
        Assert.AreEqual(56f, GetPart(bodyParts, 0, "Torso").CurrentHealth, "The next-lowest eligible part regenerates instead.");
    }

    /// <summary>
    /// The lockout ends because the simulation reached its frame, not because anything visited the
    /// part -- so a single Update on the far side of the deadline finds it selectable, however many
    /// visits did or didn't happen in between.
    /// </summary>
    [TestMethod]
    public void Update_PastItsLockoutFrame_PartIsSelectableAgain()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 50, maximumHealth: 200, isVital: true));
        SetLockout(bodyParts, 0, "Torso", 120);

        system.Update(Frame(119), 0); // Still locked out: skipped, no regen.
        Assert.AreEqual(50f, GetPart(bodyParts, 0, "Torso").CurrentHealth);

        system.Update(Frame(120), 0); // Its own frame: eligible again, regens this visit.

        Assert.AreEqual(56f, GetPart(bodyParts, 0, "Torso").CurrentHealth);
    }

    [TestMethod]
    public void Update_PartHealedAboveZero_ClearsIsDisabled()
    {
        var bodyParts = CreateBodyPartsPool();
        var system = new ComplexHealthRegenSystem(bodyParts, CreateHealthPool(), CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));
        bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, 0, 0, currentHealth: 0, maximumHealth: 200, isVital: true));
        bodyParts.UpdateByDenseIndex(bodyParts.GetFirstDenseIndex(0), static (ref BodyPartComponent part) => part.IsDisabled = true);

        system.Update(default, 0);

        Assert.IsFalse(GetPart(bodyParts, 0, "Torso").IsDisabled);
    }
}
