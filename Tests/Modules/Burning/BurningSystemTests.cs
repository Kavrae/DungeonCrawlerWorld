using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
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

    private static MultiComponentPool<StatusEffectStack> CreateStackPool() => new(maximumEntityCount: 10, initialCapacity: 10);

    private static PackedComponentPool<HealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);

    /// <summary>
    /// BurningSystem is striped (see its own doc comment), so CountdownTicker.Tick decrements
    /// FramesUntilNextTick by StripeCount per visit, not by 1 -- otherwise a striped entity's
    /// timer would take TickIntervalFrames * StripeCount real frames to fire instead of
    /// TickIntervalFrames. Pinned to Local (framesPerVisit == base StripeCount exactly, no tier
    /// divisor on top) since that's what this test is verifying -- untiered would fail open to
    /// Beyond's divisor-8 framesPerVisit instead, which is a different (and separately tested,
    /// see Update_ThrottledEntity_*) concern.
    /// </summary>
    [TestMethod]
    public void Update_CountdownDecrementsByStripeCountPerVisit()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Local));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(60 - system.StripeCount, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_AtTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        for (var i = 0; i < 7; i++)
        {
            stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        }
        timers.Add(0, new BurningTimerComponent(1, stackCount: 7));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
        Assert.AreEqual(6, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Burning));
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
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(BurningEffects.TickIntervalFrames, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

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
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void Update_AfterTimerRemoved_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);
        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 3, maximumHealth: 100));
        for (var i = 0; i < 5; i++)
        {
            stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        }
        timers.Add(0, new BurningTimerComponent(1, stackCount: 5));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void RemovingOneSourceStack_LeavesOtherSourceStackAndTimerIntact()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.FromEntity(42)));
        timers.Add(0, new BurningTimerComponent(BurningEffects.TickIntervalFrames, stackCount: 2));

        stacks.RemoveFirst(0, static (ref readonly StatusEffectStack stack) => stack.Source == StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Burning));
        Assert.IsTrue(timers.Has(0));
    }

    [TestMethod]
    public void Update_PlayerEntity_PublishesEntityDamaged()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);
        var system = new BurningSystem(timers, stacks, health, eventBus, new FakePlayerQuery(0), CreateTiersPool(), new ProcessingTierEvents());

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
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(1, new HealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(1, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(1, new BurningTimerComponent(1, stackCount: 1));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);
        var system = new BurningSystem(timers, stacks, health, eventBus, new FakePlayerQuery(playerEntityId: 0), CreateTiersPool(), new ProcessingTierEvents());

        // Entity 1 lands in stripe 1 (entityId % StripeCount), not stripe 0.
        system.Update(default, 1);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrementCountdown()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents());

        // Entity 0, Neighborhood-tiered (StripeCount 15 * divisor 2 = 30), lands in bucket 0 -- due only when FrameCount % 30 == 0.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(60, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_DecrementsCountdown()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        var tiers = CreateTiersPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), tiers, new ProcessingTierEvents());

        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        // Decremented by the Neighborhood tier's own framesPerVisit (StripeCount 15 * divisor 2 = 30), not the base StripeCount.
        Assert.AreEqual(60 - (system.StripeCount * 2), timers.GetReadonly(0).FramesUntilNextTick);
    }
}
