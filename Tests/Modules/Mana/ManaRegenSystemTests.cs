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

/// <summary>Mirrors HealthRegenSystemTests exactly in shape, with Intelligence in place of Constitution and Mana's own much smaller flat regen range in place of Health's -- see ManaRegenSystem's own doc comment for why it's built off SimpleHealthRegenSystem's exact shape.</summary>
[TestClass]
public sealed class ManaRegenSystemTests
{
    private static PackedComponentPool<ManaComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    /// <summary>Intelligence total 300 -- ManaRegenSystem's MaxManaRegenPerSecond, a flat 0.3 MP/sec -- so any entity regens a clean 0.3/visit at Local tier (StripeCount is a full second's worth of frames), or 0.6/visit at Neighborhood (120-frame/2-second cadence: 0.3 * 120/60 = 0.6).</summary>
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

        Assert.AreEqual(50.3f, pool.GetReadonly(0).CurrentMana, 0.0001f);
    }

    /// <summary>Starts already at the cap, so any positive regen (however small) must clamp back down to it -- robust against the exact flat regen amount, unlike relying on a specific amount to just barely cross a gap below the cap.</summary>
    [TestMethod]
    public void Update_ClampsAtMaximumMana()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 200, maximumMana: 200));
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
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));
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
            canModify: false, magnitude: -100000f, remainingDurationFrames: null, StatusEffectSource.Admin));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: statModifiers, abilityScores: abilityScores);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentMana);
    }

    /// <summary>
    /// Regression test for the actual reported bug: MaximumMana no longer factors into the regen
    /// amount at all now that regen is flat, but a low Intelligence total against
    /// ManaRegenSystem's own narrow MinManaRegenPerSecond-to-MaxManaRegenPerSecond range still
    /// produces a genuinely fractional per-visit amount. Rounding that to a whole point (first
    /// plainly, then via dithered/stochastic rounding once plain rounding was found to floor a low regen rate to 0
    /// forever) either stalled regen entirely or produced visible multi-tick dry streaks -- see
    /// ManaComponent's own doc comment. Storing CurrentMana as float sidesteps both: a single
    /// visit's fractional contribution lands exactly and immediately, no rounding and no luck
    /// involved.
    /// </summary>
    [TestMethod]
    public void Update_LowFractionalRate_AddsExactFractionalAmountImmediately()
    {
        var pool = CreatePool();
        pool.Add(0, new ManaComponent(currentMana: 0, maximumMana: 6));
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        abilityScores.Add(0, new AbilityScoreComponent(AbilityScoreType.Intelligence, baseValue: 6, total: 6));
        var system = new ManaRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), abilityScores: abilityScores);

        system.Update(default, 0);

        // amountPerSecond(6) = 0.1 + (6-1)/299*0.2 ~= 0.10334; a 1-second Local tick adds that
        // exact fractional amount on the very first visit, not "eventually".
        Assert.AreEqual(0.1033445f, pool.GetReadonly(0).CurrentMana, 0.0001f);
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

        Assert.AreEqual(50.6f, pool.GetReadonly(0).CurrentMana, 0.0001f);
    }
}
