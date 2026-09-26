using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Poison;

[TestClass]
public sealed class PoisonSystemTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
        public Engine.ECS.Entities.EntityKey PlayerEntityKey { get; init; } = TestSources.KeyOf(playerEntityId);
    }

    private static PackedComponentPool<PoisonTimerComponent> CreateTimerPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    [TestMethod]
    public void Update_BeforeItsTickFrame_DealsNoDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(nextTickFrame: 60, stackCount: 1, remainingDurationTicks: 5, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        for (var frame = 0; frame < 60; frame++)
        {
            system.Update(Frame(frame), 0);
        }

        Assert.AreEqual(100, health.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_AtTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 7, remainingDurationTicks: 5, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
    }

    /// <summary>The defining difference from Burning: ticking deals damage but never consumes a stack.</summary>
    [TestMethod]
    public void Update_AtTickFrame_StackCountIsUnchanged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 7, remainingDurationTicks: 5, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.AreEqual(7, timers.GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void Update_AtTickFrame_RemainingDurationDecrementsByOne()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 5, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.AreEqual(4, timers.GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void Update_LastDurationTickConsumed_RemovesTimer()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 1, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void Update_AfterExpiry_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 1, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);
        system.Update(Frame(2), 0);
    }

    [TestMethod]
    public void Update_DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 3, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 5, remainingDurationTicks: 5, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.AreEqual(0, health.GetReadonly(0).CurrentHealth);
    }

    /// <summary>One tick per interval, each on its exact frame, same damage every tick (stacks aren't consumed) until the duration runs out.</summary>
    [TestMethod]
    public void Update_MultipleTicksBeforeExpiry_DealsSameDamageEachTickUntilDurationEnds()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 4, remainingDurationTicks: 3, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0); // tick 1: duration 3 -> 2
        Assert.IsTrue(timers.Has(0));
        Assert.AreEqual(96, health.GetReadonly(0).CurrentHealth);
        Assert.AreEqual(1u + PoisonEffects.TickIntervalFrames, timers.GetReadonly(0).NextTickFrame);

        Run(system, 2, 1 + PoisonEffects.TickIntervalFrames); // tick 2: duration 2 -> 1, still alive
        Assert.IsTrue(timers.Has(0));
        Assert.AreEqual(92, health.GetReadonly(0).CurrentHealth);

        Run(system, 2 + PoisonEffects.TickIntervalFrames, 1 + 2 * PoisonEffects.TickIntervalFrames); // tick 3: duration 1 -> 0, expires
        Assert.IsFalse(timers.Has(0));
        Assert.AreEqual(88, health.GetReadonly(0).CurrentHealth);
    }

    /// <summary>Runs every frame from..to inclusive -- the way SystemManager drives it.</summary>
    private static void Run(PoisonSystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    [TestMethod]
    public void Update_PlayerEntity_PublishesEntityDamagedWithCachedSource()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 5, ActionSource.Admin));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);
        var system = new PoisonSystem(timers, health, eventBus, new FakePlayerQuery(0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.Amount);
        Assert.AreEqual(ActionSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Poison)", published.Value.DamageType);
    }

    [TestMethod]
    public void Update_NonPlayerEntity_DoesNotPublishEntityDamaged()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(1, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(1, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 5, ActionSource.Admin));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);
        var system = new PoisonSystem(timers, health, eventBus, new FakePlayerQuery(playerEntityId: 0), new MathUtility());

        system.Update(Frame(1), 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Update_ConditionalIncomingDamageDebuffScopedToPoison_ReducesDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 10, remainingDurationTicks: 5, ActionSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin, Tag.Poison));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(), statModifiers);

        system.Update(Frame(1), 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "10 * 0.5 = 5 damage taken.");
    }

    [TestMethod]
    public void Update_UnconditionalIncomingDamageDebuff_StillReducesPoisonDamage()
    {
        var timers = CreateTimerPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 10, remainingDurationTicks: 5, ActionSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(), statModifiers);

        system.Update(Frame(1), 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "Unconditional IncomingDamage debuffs apply regardless of ConditionTag.");
    }

    /// <summary>Complex target: Poison always aims at Internal (BodyPartTargetRule(Internal, Random)), never scattering across other parts the way Burning's own random-part-per-tick does -- run across several seeds since a bug here would only sometimes land wrong.</summary>
    [TestMethod]
    public void Update_ComplexTarget_DamageAlwaysLandsOnInternal()
    {
        for (var seed = 0; seed < 10; seed++)
        {
            var timers = CreateTimerPool();
            var health = CreateHealthPool();
            var bodyParts = BodyPartTestWorld.WithParts(0, ("Head", BodyPartType.Head, 40, 40, true), ("Torso", BodyPartType.Torso, 65, 65, true), ("Internal", BodyPartType.Internal, 15, 15, true)).BodyParts;
            timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 5, ActionSource.Admin));
            var system = new PoisonSystem(timers, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(new Random(seed)), statModifiers: null, bodyParts: bodyParts);

            system.Update(Frame(1), 0);

            Assert.AreEqual(40f, GetPartHealth(bodyParts, 0, "Head"), $"Seed {seed}: Head must be untouched.");
            Assert.AreEqual(65f, GetPartHealth(bodyParts, 0, "Torso"), $"Seed {seed}: Torso must be untouched.");
            Assert.AreEqual(12f, GetPartHealth(bodyParts, 0, "Internal"), $"Seed {seed}: Internal must always take the hit.");
        }
    }

    private static float GetPartHealth(EntityBodyParts bodyParts, int entityId, string name)
    {
        foreach (var part in bodyParts.Parts(entityId))
        {
            if (part.Name == name)
            {
                return part.CurrentHealth;
            }
        }

        throw new InvalidOperationException("No part named " + name + ".");
    }
}
