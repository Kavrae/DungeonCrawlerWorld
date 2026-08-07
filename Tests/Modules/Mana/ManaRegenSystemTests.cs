using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Mana.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Mana;

/// <summary>Mirrors HealthRegenSystemTests exactly (same numbers even), with Intelligence in place of Constitution -- see ManaRegenSystem's own doc comment for why it's built off HealthRegenSystem's exact shape.</summary>
[TestClass]
public sealed class ManaRegenSystemTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    /// <summary>Intelligence total 300 -- AbilityScoreRegenMath's top rate, 6%/sec -- so a 200-max entity regens a clean 12/visit at Local tier (StripeCount is a full second's worth of frames: 0.06 * 200 * 60/60 = 12), or 24/visit at Neighborhood (120-frame/2-second cadence: 0.06 * 200 * 120/60 = 24).</summary>
    private static MultiComponentPool<AbilityScoreComponent> CreateAbilityScoresPoolWithMaxIntelligence(int entityId)
    {
        var pool = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        pool.Add(entityId, new AbilityScoreComponent(AbilityScoreType.Intelligence, baseValue: 300, total: 300));
        return pool;
    }

    [TestMethod]
    public void Update_RegeneratesManaByLiveComputedIntelligenceAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 200));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxIntelligence(0));

        system.Update(default, 0);

        Assert.AreEqual(62, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumMana()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 199, maximumMana: 200));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxIntelligence(0));

        system.Update(default, 0);

        Assert.AreEqual(200, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotRegenerate()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 0, maximumMana: 200));
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities, abilityScores: CreateAbilityScoresPoolWithMaxIntelligence(0));

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_NoAbilityScorePool_LeavesCurrentManaUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 200));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_NoIntelligenceScoreForEntity_LeavesCurrentManaUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 200));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_LargeNegativeManaRegenModifier_ClampsToZeroInsteadOfUnderflowWrapping()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: -32000, maximumMana: 200));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        abilityScores.Add(0, new AbilityScoreComponent(AbilityScoreType.Intelligence, baseValue: 1, total: 1));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.ManaRegen, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
            canModify: false, magnitude: -100000f, remainingDurationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: statModifiers, abilityScores: abilityScores);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentMana);
    }

    /// <summary>
    /// Regression test for the actual reported bug: a starting player's MaximumMana equals
    /// Intelligence's Total, typically just 2-12 for a 2d6 roll -- Intelligence 6 here gives
    /// ~2.07%/sec, and 2.07% of a MaximumMana of 6 is ~0.124/visit. Rounding that to a whole
    /// point (first plainly, then via dithered/stochastic rounding once plain rounding was found
    /// to floor this to 0 forever) either stalled regen entirely or produced visible multi-tick
    /// dry streaks -- see ManaComponent's own doc comment. Storing CurrentMana as float
    /// sidesteps both: a single visit's fractional contribution lands exactly and immediately,
    /// no rounding and no luck involved.
    /// </summary>
    [TestMethod]
    public void Update_LowFractionalRateAgainstSmallMaximumMana_AddsExactFractionalAmountImmediately()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 0, maximumMana: 6));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        abilityScores.Add(0, new AbilityScoreComponent(AbilityScoreType.Intelligence, baseValue: 6, total: 6));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);

        system.Update(default, 0);

        // percentPerSecond(6) = 2 + (6-1)/299*4 ~= 2.0669%; 2.0669% of 6 over a 1-second Local
        // tick ~= 0.12401 -- the exact amount lands on the very first visit, not "eventually".
        Assert.AreEqual(0.1240134f, pool.GetReadonly(0).CurrentMana, 0.0001f);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotRegenerate()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new ManaRegenSystem(pool, tiers, new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxIntelligence(0));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentMana);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Regenerates()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new ManaComponent(currentMana: 50, maximumMana: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new ManaRegenSystem(pool, tiers, new ProcessingTierEvents(), abilityScores: CreateAbilityScoresPoolWithMaxIntelligence(0));

        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.AreEqual(74, pool.GetReadonly(0).CurrentMana);
    }
}
