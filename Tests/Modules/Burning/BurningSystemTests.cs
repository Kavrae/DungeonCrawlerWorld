using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Burning;

[TestClass]
public sealed class BurningSystemTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static PackedComponentPool<BurningTimerComponent> CreateTimerPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Seeds entity 0's tier explicitly -- see ContactDamageSystemTests.CreateTiersPool's own note. Tests wanting another tier Merge over it rather than Add, since Add throws on an entity that already has one.</summary>
    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool(ProcessingTierLevel tier = ProcessingTierLevel.Local)
    {
        var pool = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        pool.Add(0, new ProcessingTierComponent(tier));
        return pool;
    }

    /// <summary>
    /// BurningSystem is striped (see its own doc comment), so CountdownTicker.Tick decrements
    /// FramesUntilNextTick by StripeCount per visit, not by 1 -- otherwise a striped entity's
    /// timer would take TickIntervalFrames * StripeCount real frames to fire instead of
    /// TickIntervalFrames. Pinned to Local (framesPerVisit == base StripeCount exactly, no tier
    /// divisor on top) since that's what this test is verifying -- untiered would fail open to
    /// Beyond's much coarser framesPerVisit instead, which is a different (and separately tested,
    /// see Update_ThrottledEntity_*) concern.
    /// </summary>
    [TestMethod]
    public void Update_CountdownDecrementsByStripeCountPerVisit()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool();
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1, StatusEffectSource.Admin));
        tiers.Merge(0, new ProcessingTierComponent(ProcessingTierLevel.Local));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(60 - system.StripeCount, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_AtTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 7, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
        Assert.AreEqual(6, timers.GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void Update_ConditionalIncomingDamageDebuffScopedToFire_ReducesDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 10, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.Admin, Tag.Fire));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "10 * 0.5 = 5 damage taken.");
    }

    [TestMethod]
    public void Update_UnconditionalIncomingDamageDebuff_StillReducesBurningDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 10, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "Unconditional IncomingDamage debuffs apply regardless of ConditionTag.");
    }

    /// <summary>
    /// Regression test for the striping-cadence bug: a striped entity (see BurningSystem's own
    /// doc comment) must still tick exactly once every TickIntervalFrames real Update calls --
    /// not once every TickIntervalFrames * StripeCount, which decrementing by 1 per visit
    /// instead of by StripeCount would cause. Rotates stripeIndex across all of BurningSystem's
    /// stripes the same way SystemManager does in real play, rather than calling Update with a
    /// fixed stripeIndex every time (entity 0 always lands in stripe 0 regardless of
    /// StripeCount, so a fixed-stripeIndex loop wouldn't actually exercise striping at all).
    /// </summary>
    [TestMethod]
    public void Update_SixtyCallsFromFreshTimer_TicksExactlyOnce()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(BurningEffects.TickIntervalFrames, stackCount: 1, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        for (var frame = 0; frame < BurningEffects.TickIntervalFrames; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
        }

        Assert.AreEqual(99, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_LastStackConsumed_RemovesBurningTimerComponent()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void Update_AfterTimerRemoved_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);
        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 3, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 5, StatusEffectSource.Admin));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(0, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_PlayerEntity_PublishesEntityDamaged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);
        var system = new BurningSystem(timers, health, eventBus, new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        system.Update(default, 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.Amount);
        Assert.AreEqual(StatusEffectSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Burning)", published.Value.DamageType);
    }

    [TestMethod]
    public void Update_NonPlayerEntity_DoesNotPublishEntityDamaged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(1, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(1, new BurningTimerComponent(1, stackCount: 1, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);
        var system = new BurningSystem(timers, health, eventBus, new FakePlayerQuery(playerEntityId: 0), CreateTiersPool(), new ProcessingTierEvents(), new MathUtility());

        // Entity 1 lands in stripe 1 (entityId % StripeCount), not stripe 0.
        system.Update(default, 1);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrementCountdown()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool();
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1, StatusEffectSource.Admin));
        tiers.Merge(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents(), new MathUtility());

        // Entity 0, Neighborhood-tiered (StripeCount * the Neighborhood divisor) lands in bucket 0 -- due only when FrameCount is a multiple of that product.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(60, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_DecrementsCountdown()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool(ProcessingTierLevel.Neighborhood);
        var system = new BurningSystem(timers, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents(), new MathUtility());

        // Derived, not hardcoded, so re-tuning ProcessingTierDivisors doesn't invalidate this.
        // The timer is seeded longer than one visit's span deliberately: this test covers the
        // decrement path, and a countdown shorter than framesPerVisit would instead fire (and,
        // at stackCount 1, remove the component outright) -- that catch-up behaviour is
        // CountdownTicker's own concern, covered separately. Added after construction because
        // the stripe set tracks the driving pool's EntityAdded, so membership and tier still
        // resolve correctly.
        var framesPerVisit = system.StripeCount * ProcessingTierDivisors.ByTierIndex[(int)ProcessingTierLevel.Neighborhood];
        var startingCountdown = (ushort)(framesPerVisit + 60);
        timers.Add(0, new BurningTimerComponent(startingCountdown, stackCount: 1, StatusEffectSource.Admin));

        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        // Decremented by the Neighborhood tier's own framesPerVisit, not the base StripeCount.
        Assert.AreEqual(startingCountdown - framesPerVisit, timers.GetReadonly(0).FramesUntilNextTick);
    }
}
