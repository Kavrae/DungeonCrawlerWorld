using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Health.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Poison;

[TestClass]
public sealed class PoisonSystemTests
{
    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static PackedComponentPool<PoisonTimerComponent> CreateTimerPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    private static MultiComponentPool<StatusEffectStack> CreateStackPool() => new(maximumEntityCount: 10, initialCapacity: 10);

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_CountdownDecrementsByOnePerCall()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        timers.Add(0, new PoisonTimerComponent(60, stackCount: 1, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(59, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_AtTickFrame_DamageEqualsStackCount_NotSquared()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 7, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(93, health.GetReadonly(0).CurrentHealth);
    }

    /// <summary>The defining difference from Burning: ticking deals damage but never consumes a stack.</summary>
    [TestMethod]
    public void Update_AtTickFrame_StackCountIsUnchanged()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 7, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(7, timers.GetReadonly(0).StackCount);
    }

    [TestMethod]
    public void Update_AtTickFrame_RemainingDurationDecrementsByOne()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(4, timers.GetReadonly(0).RemainingDurationTicks);
    }

    [TestMethod]
    public void Update_LastDurationTickConsumed_RemovesTimerAndAllStacks()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Poison, StatusEffectSource.Admin));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Poison, StatusEffectSource.Admin));
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Poison, StatusEffectSource.Admin));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 1, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
        Assert.AreEqual(0, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Poison));
    }

    [TestMethod]
    public void Update_AfterExpiry_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 1, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);
        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_DamageClampsAtZero()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 3, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 5, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.AreEqual(0, health.GetReadonly(0).CurrentHealth);
    }

    /// <summary>
    /// Each tick only actually fires once every TickIntervalFrames real Update calls (see
    /// BurningSystemTests' equivalent striping-cadence regression test) -- RunFullCycle drives
    /// exactly one tick's worth of real frames between assertions.
    /// </summary>
    [TestMethod]
    public void Update_MultipleTicksBeforeExpiry_DealsSameDamageEachTickUntilDurationEnds()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 4, remainingDurationTicks: 3, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0); // FramesUntilNextTick starts at 1 -- tick 1 fires immediately: duration 3 -> 2
        Assert.IsTrue(timers.Has(0));
        Assert.AreEqual(96, health.GetReadonly(0).CurrentHealth);

        RunFullCycle(system); // tick 2: duration 2 -> 1, still alive
        Assert.IsTrue(timers.Has(0));
        Assert.AreEqual(92, health.GetReadonly(0).CurrentHealth);

        RunFullCycle(system); // tick 3: duration 1 -> 0, expires
        Assert.IsFalse(timers.Has(0));
        Assert.AreEqual(88, health.GetReadonly(0).CurrentHealth);
    }

    private static void RunFullCycle(PoisonSystem system)
    {
        for (var frame = 0; frame < PoisonEffects.TickIntervalFrames; frame++)
        {
            system.Update(default, 0);
        }
    }

    [TestMethod]
    public void Update_PlayerEntity_PublishesEntityDamagedWithCachedSource()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        EntityDamagedEvent? published = null;
        eventBus.Subscribe<EntityDamagedEvent>(e => published = e);
        var system = new PoisonSystem(timers, stacks, health, eventBus, new FakePlayerQuery(0), new MathUtility());

        system.Update(default, 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(1, published!.Value.Amount);
        Assert.AreEqual(StatusEffectSource.Admin, published.Value.Source);
        Assert.AreEqual("Status Effect (Poison)", published.Value.DamageType);
    }

    [TestMethod]
    public void Update_NonPlayerEntity_DoesNotPublishEntityDamaged()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(1, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(1, new PoisonTimerComponent(1, stackCount: 1, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var eventBus = new EventBus();
        var published = false;
        eventBus.Subscribe<EntityDamagedEvent>(_ => published = true);
        var system = new PoisonSystem(timers, stacks, health, eventBus, new FakePlayerQuery(playerEntityId: 0), new MathUtility());

        system.Update(default, 0);

        Assert.IsFalse(published);
    }

    [TestMethod]
    public void Update_ConditionalIncomingDamageDebuffScopedToPoison_ReducesDamage()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 10, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.Admin, Tag.Poison));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "10 * 0.5 = 5 damage taken.");
    }

    [TestMethod]
    public void Update_UnconditionalIncomingDamageDebuff_StillReducesPoisonDamage()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        var health = CreateHealthPool();
        health.Add(0, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        timers.Add(0, new PoisonTimerComponent(1, stackCount: 10, remainingDurationTicks: 5, StatusEffectSource.Admin));
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(0, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.Admin));
        var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(), statModifiers);

        system.Update(default, 0);

        Assert.AreEqual(95, health.GetReadonly(0).CurrentHealth, "Unconditional IncomingDamage debuffs apply regardless of ConditionTag.");
    }

    /// <summary>Complex target: Poison always aims at Internal (BodyPartTargetRule(Internal, Random)), never scattering across other parts the way Burning's own random-part-per-tick does -- run across several seeds since a bug here would only sometimes land wrong.</summary>
    [TestMethod]
    public void Update_ComplexTarget_DamageAlwaysLandsOnInternal()
    {
        for (var seed = 0; seed < 10; seed++)
        {
            var timers = CreateTimerPool();
            var stacks = CreateStackPool();
            var health = CreateHealthPool();
            var bodyParts = new MultiComponentPool<BodyPartComponent>(maximumEntityCount: 10, initialCapacity: 8);
            bodyParts.Add(0, new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, currentHealth: 40, maximumHealth: 40, isVital: true));
            bodyParts.Add(0, new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, currentHealth: 65, maximumHealth: 65, isVital: true));
            bodyParts.Add(0, new BodyPartComponent("Internal", BodyPartType.Internal, partId: 2, verticalPosition: 4, currentHealth: 15, maximumHealth: 15, isVital: true));
            timers.Add(0, new PoisonTimerComponent(1, stackCount: 3, remainingDurationTicks: 5, StatusEffectSource.Admin));
            var system = new PoisonSystem(timers, stacks, health, new EventBus(), new FakePlayerQuery(0), new MathUtility(new Random(seed)), statModifiers: null, bodyParts);

            system.Update(default, 0);

            Assert.AreEqual(40f, GetPartHealth(bodyParts, 0, "Head"), $"Seed {seed}: Head must be untouched.");
            Assert.AreEqual(65f, GetPartHealth(bodyParts, 0, "Torso"), $"Seed {seed}: Torso must be untouched.");
            Assert.AreEqual(12f, GetPartHealth(bodyParts, 0, "Internal"), $"Seed {seed}: Internal must always take the hit.");
        }
    }

    private static float GetPartHealth(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, string name)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            var part = bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.Name == name)
            {
                return part.CurrentHealth;
            }
        }

        return float.NaN;
    }
}
