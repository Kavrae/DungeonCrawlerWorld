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

[TestClass]
public sealed class SimpleHealthRegenSystemTests
{
    private static PackedComponentPool<SimpleHealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    /// <summary>Constitution total 300 -- SimpleHealthRegenSystem's MaxHealthRegenPerSecond, a flat 6 HP/sec -- so any entity regens a clean 6/visit at Local tier (StripeCount is a full second's worth of frames), or 12/visit at Neighborhood (120-frame/2-second cadence: 6 * 120/60 = 12).</summary>
    private static MultiComponentPool<AbilityScoreComponent> CreateAbilityScoresPoolWithMaxConstitution(int entityId)
    {
        var pool = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(entityId, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
        return pool;
    }

    [TestMethod]
    public void Update_RegeneratesHealthByLiveComputedConstitutionAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        system.Update(default, 0);

        Assert.AreEqual(56, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 199, maximumHealth: 200));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        system.Update(default, 0);

        Assert.AreEqual(200, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotRegenerate()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 0, maximumHealth: 200));
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities, abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_NoAbilityScorePool_LeavesCurrentHealthUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_NoConstitutionScoreForEntity_LeavesCurrentHealthUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    /// <summary>
    /// Regression test: CurrentHealth += effectiveRegen used to compute in short and could
    /// silently overflow/underflow before the subsequent clamp ran. A large negative
    /// HealthRegen modifier against a very negative CurrentHealth underflows short's range and
    /// wraps to a large positive number -- if that wrapped value were what got clamped, it would
    /// land near MaximumHealth instead of the mathematically correct 0.
    /// </summary>
    [TestMethod]
    public void Update_LargeNegativeHealthRegenModifier_ClampsToZeroInsteadOfUnderflowWrapping()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: -32000, maximumHealth: 200));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        abilityScores.Add(0, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 1, total: 1));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: false, magnitude: -100000f, remainingDurationFrames: null, StatusEffectSource.Admin));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: statModifiers, abilityScores: abilityScores);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotRegenerate()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new SimpleHealthRegenSystem(pool, tiers, new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        // Entity 0, Neighborhood-tiered (StripeCount 60 * divisor 2 = 120), lands in bucket 0 -- due only when FrameCount % 120 == 0.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    /// <summary>Regen is now routed through HealthHeal.Apply (sourceEntityId: entityId, a self-heal) -- an IncomingHealing modifier scales the regen tick the same way it would scale a potion or spell.</summary>
    [TestMethod]
    public void Update_IncomingHealingModifier_ScalesRegenTick()
    {
        var pool = CreatePool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingHealing, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));
        var system = new SimpleHealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers, abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        system.Update(default, 0);

        Assert.AreEqual(59, pool.GetReadonly(0).CurrentHealth, "6 base regen * 1.5 = 9; 50 + 9 = 59.");
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Regenerates()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new SimpleHealthRegenSystem(pool, tiers, new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxConstitution(0));

        // Neighborhood cadence is twice Local's (120 frames/2 seconds vs 60 frames/1 second), so
        // the per-visit amount is proportionally larger too: 6 * 120/60 = 12, not the 6 a
        // Local-tier visit gets.
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.AreEqual(62, pool.GetReadonly(0).CurrentHealth);
    }
}
