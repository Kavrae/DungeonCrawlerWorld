using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatModifiers.Systems;
using Game.World;
using Game.Blueprints;

namespace Tests.Modules.Health;

[TestClass]
public sealed class MaximumHealthShiftTests
{
    private const int EntityId = 0;

    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return componentManager;
    }

    private static void GrantFiftyPercentBuff(ComponentManager componentManager, uint expiresAtFrame = FrameDeadline.Never) =>
        MaximumHealthShift.ApplyModifier(componentManager, Creatures(componentManager), EntityId, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame, ActionSource.Admin);

    [TestMethod]
    public void ApplyModifier_SimpleHealthAtFull_HealsByTheWholeIncrease()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(EntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));

        GrantFiftyPercentBuff(componentManager);

        Assert.AreEqual(150f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    /// <summary>Missing health is what survives the change: 40 short before, 40 short after.</summary>
    [TestMethod]
    public void ApplyModifier_SimpleHealthWhileHurt_KeepsTheSameAmountMissing()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(EntityId, new SimpleHealthComponent(currentHealth: 60, maximumHealth: 100));

        GrantFiftyPercentBuff(componentManager);

        Assert.AreEqual(110f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    [TestMethod]
    public void ApplyModifier_BodyParts_HealsEachPartByItsOwnIncrease()
    {
        var componentManager = CreateComponentManager();
        var bodyParts = BodyPartTestWorld.WithParts(componentManager, EntityId, ("Head", BodyPartType.Head, 40, 40, true), ("Torso", BodyPartType.Torso, 30, 60, true)).BodyParts;

        GrantFiftyPercentBuff(componentManager);

        Assert.AreEqual(60f, PartHealth(bodyParts, "Head"), 0.001f, "40 -> 60, the whole of its own increase.");
        Assert.AreEqual(60f, PartHealth(bodyParts, "Torso"), 0.001f, "30 short of 60 before, 30 short of 90 after.");
    }

    [TestMethod]
    public void Expiry_TakesBackExactlyWhatTheBuffGave()
    {
        var componentManager = CreateComponentManager();
        var eventBus = new EventBus();
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(EntityId, new SimpleHealthComponent(currentHealth: 60, maximumHealth: 100));
        var expiry = BuildExpirySystem(componentManager, eventBus);

        GrantFiftyPercentBuff(componentManager, expiresAtFrame: 10);
        Assert.AreEqual(110f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);

        expiry.Update(new EngineTime(default, default, false, FrameCount: 10), 0);

        Assert.AreEqual(60f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    /// <summary>The cycling exploit this rule exists to prevent: buff while hurt, let it lapse, and you are exactly as hurt as you were.</summary>
    [TestMethod]
    public void Expiry_AfterRepeatedCycles_NeverAccumulatesFreeHealing()
    {
        var componentManager = CreateComponentManager();
        var eventBus = new EventBus();
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(EntityId, new SimpleHealthComponent(currentHealth: 40, maximumHealth: 100));
        var expiry = BuildExpirySystem(componentManager, eventBus);

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            var deadline = (uint)(cycle * 10);
            GrantFiftyPercentBuff(componentManager, expiresAtFrame: deadline);
            expiry.Update(new EngineTime(default, default, false, FrameCount: deadline), 0);
        }

        Assert.AreEqual(40f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    /// <summary>A buff wearing off is bookkeeping, not a hit -- nothing should see it as damage.</summary>
    [TestMethod]
    public void Expiry_PublishesNoDamageEvent()
    {
        var componentManager = CreateComponentManager();
        var eventBus = new EventBus();
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(EntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        var expiry = BuildExpirySystem(componentManager, eventBus);

        var damageEvents = 0;
        var healEvents = 0;
        eventBus.Subscribe<EntityDamagedEvent>(_ => damageEvents++);
        eventBus.Subscribe<EntityHealedEvent>(_ => healEvents++);

        GrantFiftyPercentBuff(componentManager, expiresAtFrame: 10);
        expiry.Update(new EngineTime(default, default, false, FrameCount: 10), 0);

        Assert.AreEqual(0, damageEvents, "A MaximumHealth buff lapsing is not damage.");
        Assert.AreEqual(0, healEvents, "Nor is granting one a heal.");
    }

    /// <summary>Losing a buff cannot be a way to die, so the shift stops at 1 rather than running a part to 0.</summary>
    [TestMethod]
    public void Expiry_WhenTheEntityIsBelowWhatItWouldGiveBack_StopsAtOne()
    {
        var componentManager = CreateComponentManager();
        var eventBus = new EventBus();
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(EntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        var expiry = BuildExpirySystem(componentManager, eventBus);

        GrantFiftyPercentBuff(componentManager, expiresAtFrame: 10);
        health.TryUpdate(EntityId, static (ref SimpleHealthComponent component) => component.CurrentHealth = 10f);

        expiry.Update(new EngineTime(default, default, false, FrameCount: 10), 0);

        Assert.AreEqual(1f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    /// <summary>Two buffs lapsing on the same frame give back both their shares, not one of them twice.</summary>
    [TestMethod]
    public void Expiry_TwoBuffsOnTheSameFrame_GiveBackBothShares()
    {
        var componentManager = CreateComponentManager();
        var eventBus = new EventBus();
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(EntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        var expiry = BuildExpirySystem(componentManager, eventBus);

        GrantFiftyPercentBuff(componentManager, expiresAtFrame: 10);
        MaximumHealthShift.ApplyModifier(componentManager, Creatures(componentManager), EntityId, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 20f, expiresAtFrame: 10, ActionSource.Admin);

        Assert.AreEqual(180f, health.GetReadonly(EntityId).CurrentHealth, 0.001f, "(100 + 20) * 1.5 = 180.");

        expiry.Update(new EngineTime(default, default, false, FrameCount: 10), 0);

        Assert.AreEqual(100f, health.GetReadonly(EntityId).CurrentHealth, 0.001f);
    }

    private static StatModifierExpirySystem BuildExpirySystem(ComponentManager componentManager, EventBus eventBus)
    {

        var system = new StatModifierExpirySystem(
            componentManager.GetMultiPool<StatModifierComponent>(),
            componentManager.GetPackedPool<ExpiringStatModifierComponent>(),
            eventBus);

        WireShift(componentManager, eventBus);
        return system;
    }

    /// <summary>The same two subscriptions HealthModule.WireMaximumHealthShift makes, without building a whole module set.</summary>
    private static void WireShift(ComponentManager componentManager, EventBus eventBus)
    {
        var expiringEntityId = -1;
        var additiveSum = 0f;
        var multiplicativeSum = 0f;

        eventBus.Subscribe<StatModifierExpiringEvent>(expiring =>
        {
            expiringEntityId = expiring.EntityId;
            MaximumHealthShift.Capture(componentManager, expiring.EntityId, out additiveSum, out multiplicativeSum);
        });

        eventBus.Subscribe<StatModifierExpiredEvent>(expired =>
        {
            if (expired.Target != StatModifierTarget.MaximumHealth || expired.EntityId != expiringEntityId)
            {
                return;
            }

            expiringEntityId = -1;
            MaximumHealthShift.Apply(componentManager, Creatures(componentManager), expired.EntityId, additiveSum, multiplicativeSum);
        });
    }

    /// <summary>The definitions the body parts in this fixture were registered in -- see BodyPartTestWorld.</summary>
    private static BlueprintRegistry Creatures(ComponentManager componentManager) => BodyPartTestWorld.CreaturesOf(componentManager);

    private static float PartHealth(EntityBodyParts bodyParts, string name)
    {
        foreach (var part in bodyParts.Parts(EntityId))
        {
            if (part.Name == name)
            {
                return part.CurrentHealth;
            }
        }

        throw new InvalidOperationException($"No body part named {name}.");
    }
}
