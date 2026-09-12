using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
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

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static PackedComponentPool<BurningTimerComponent> CreateTimerPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static BurningSystem CreateSystem(PackedComponentPool<BurningTimerComponent> timers, PackedComponentPool<SimpleHealthComponent> health, EventBus? eventBus = null, MultiComponentPool<StatModifierComponent>? statModifiers = null, int playerEntityId = 0) =>
        new(timers, health, eventBus ?? new EventBus(), new FakePlayerQuery(playerEntityId), new MathUtility(), statModifiers);

    private static void Run(BurningSystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    [TestMethod]
    public void BeforeItsTickFrame_DealsNoDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 60, stackCount: 1, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        Run(system, 0, 59);

        Assert.AreEqual(100, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void OnItsTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 7, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        system.Update(Frame(1), 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
        Assert.AreEqual(6, timers.GetReadonly(0).StackCount);
        Assert.AreEqual(1u + BurningEffects.TickIntervalFrames, timers.GetReadonly(0).NextTickFrame, "Re-armed one interval after the tick.");
    }

    /// <summary>One tick per interval, each on its exact frame, one stack lost per tick -- 3 + 2 + 1 damage, then gone.</summary>
    [TestMethod]
    public void TicksEveryIntervalUntilTheLastStack()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: BurningEffects.TickIntervalFrames, stackCount: 3, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        Run(system, 0, 2 * BurningEffects.TickIntervalFrames - 1);
        Assert.AreEqual(97, health.GetReadonly(0).CurrentHealth, "Only the first tick so far.");

        Run(system, 2 * BurningEffects.TickIntervalFrames, 3 * BurningEffects.TickIntervalFrames);
        Assert.AreEqual(94, health.GetReadonly(0).CurrentHealth);
        Assert.IsFalse(timers.Has(0));
    }

    /// <summary>A new burn is scheduled by BurningEffects.ApplyStack adding the component -- nothing tells BurningSystem -- and its first tick lands one interval after the frame it started.</summary>
    [TestMethod]
    public void BurnStartedMidRun_FirstTicksOneIntervalAfterItStarted()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        var system = CreateSystem(componentManager.GetPackedPool<BurningTimerComponent>(), health);
        Run(system, 0, 100);

        BurningEffects.ApplyStack(componentManager, 0, StatusEffectSource.Admin, now: 100);

        Run(system, 101, 100 + BurningEffects.TickIntervalFrames - 1);
        Assert.AreEqual(100, health.GetReadonly(0).CurrentHealth);

        system.Update(Frame(100 + BurningEffects.TickIntervalFrames), 0);
        Assert.AreEqual(99, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void ConditionalIncomingDamageDebuffScopedToFire_ReducesDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 10, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.Admin, Tag.Fire));
        var system = CreateSystem(timers, health, statModifiers: statModifiers);

        system.Update(Frame(1), 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "10 * 0.5 = 5 damage taken.");
    }

    [TestMethod]
    public void UnconditionalIncomingDamageDebuff_StillReducesBurningDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 10, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health, statModifiers: statModifiers);

        system.Update(Frame(1), 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "Unconditional IncomingDamage debuffs apply regardless of ConditionTag.");
    }

    [TestMethod]
    public void LastStackConsumed_RemovesBurningTimerComponent()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 1, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        system.Update(Frame(1), 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void AfterTimerRemoved_FurtherUpdatesDoNotThrow()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 1, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        Run(system, 0, 5);

        Assert.AreEqual(99, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 3, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 5, StatusEffectSource.Admin));
        var system = CreateSystem(timers, health);

        system.Update(Frame(1), 0);

        Assert.AreEqual(0, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void PlayerEntity_PublishesEntityDamaged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new BurningTimerComponent(nextTickFrame: 1, stackCount: 1, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);
        var system = CreateSystem(timers, health, eventBus);

        system.Update(Frame(1), 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.Amount);
        Assert.AreEqual(StatusEffectSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Burning)", published.Value.DamageType);
    }

    [TestMethod]
    public void NonPlayerEntity_DoesNotPublishEntityDamaged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(1, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(1, new BurningTimerComponent(nextTickFrame: 1, stackCount: 1, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);
        var system = CreateSystem(timers, health, eventBus, playerEntityId: 0);

        system.Update(Frame(1), 0);

        Assert.AreEqual(99, health.GetReadonly(1).CurrentHealth, "It did tick.");
        Assert.IsFalse(published);
    }
}
