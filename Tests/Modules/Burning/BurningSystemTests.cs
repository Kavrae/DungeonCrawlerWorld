using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
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

    [TestMethod]
    public void Update_CountdownDecrementsByOnePerCall()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(60, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

        system.Update(default, 0);

        Assert.AreEqual(59, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_AtTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        for (var i = 0; i < 7; i++)
        {
            stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        }
        timers.Add(0, new BurningTimerComponent(1, stackCount: 7));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

        system.Update(default, 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
        Assert.AreEqual(6, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Burning));
    }

    /// <summary>
    /// Regression test for the striping-cadence bug the plan called out: StripeCount must be 1
    /// so a single burning entity ticks exactly once every TickIntervalFrames real Update
    /// calls, not once every TickIntervalFrames * StripeCount.
    /// </summary>
    [TestMethod]
    public void Update_SixtyCallsFromFreshTimer_TicksExactlyOnce()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(BurningEffects.TickIntervalFrames, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

        for (var frame = 0; frame < BurningEffects.TickIntervalFrames; frame++)
        {
            system.Update(default, 0);
        }

        Assert.AreEqual(99, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_LastStackConsumed_RemovesBurningTimerComponent()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void Update_AfterTimerRemoved_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

        system.Update(default, 0);
        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new HealthComponent(currentHealth: 3, healthRegen: 0, maximumHealth: 100));
        for (var i = 0; i < 5; i++)
        {
            stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        }
        timers.Add(0, new BurningTimerComponent(1, stackCount: 5));
        var system = new BurningSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0));

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
        health.Add(0, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(0, new BurningTimerComponent(1, stackCount: 1));
        var eventBus = new EventBus();
        EntityDamaged? published = null;
        eventBus.Subscribe<EntityDamaged>(e => published = e);
        var system = new BurningSystem(timers, stacks, health, eventBus, new FakePlayerQuery(0));

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
        health.Add(1, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        stacks.Add(1, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        timers.Add(1, new BurningTimerComponent(1, stackCount: 1));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamaged>(_ => published = true);
        var system = new BurningSystem(timers, stacks, health, eventBus, new FakePlayerQuery(playerEntityId: 0));

        system.Update(default, 0);

        Assert.IsFalse(published);
    }
}
