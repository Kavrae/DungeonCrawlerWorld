using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatModifiers.Systems;
using Game.World;

namespace Tests.Modules.StatModifiers;

[TestClass]
public sealed class StatModifierExpirySystemTests
{
    private static MultiComponentPool<StatModifierComponent> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    private static StatModifierComponent Modifier(int remainingDurationFrames) =>
        new(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, StatModifierPolarity.Buff, canModify: false, magnitude: 1f, remainingDurationFrames, StatusEffectSource.Admin);

    [TestMethod]
    public void Update_DecrementsRemainingDurationFramesByOne()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(5));
        var system = new StatModifierExpirySystem(pool, CreateTiersPool(), new ProcessingTierEvents(), new EventBus());

        system.Update(default, 0);

        Assert.AreEqual(4, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_AtZero_RemovesModifier()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(0));
        var system = new StatModifierExpirySystem(pool, CreateTiersPool(), new ProcessingTierEvents(), new EventBus());

        system.Update(default, 0);

        Assert.AreEqual(-1, pool.GetFirstDenseIndex(0));
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrement()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, Modifier(5));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new StatModifierExpirySystem(pool, tiers, new ProcessingTierEvents(), new EventBus());

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(5, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Decrements()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, Modifier(5));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new StatModifierExpirySystem(pool, tiers, new ProcessingTierEvents(), new EventBus());

        system.Update(new EngineTime(default, default, false, FrameCount: 2), 0);

        Assert.AreEqual(4, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_ModifierExpires_PublishesStatModifierExpiredEventWithEntityIdAndTarget()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(1));
        var eventBus = new EventBus();
        var system = new StatModifierExpirySystem(pool, CreateTiersPool(), new ProcessingTierEvents(), eventBus);
        StatModifierExpiredEvent? published = null;
        eventBus.Subscribe<StatModifierExpiredEvent>(evt => published = evt);

        system.Update(default, 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(0, published.Value.EntityId);
        Assert.AreEqual(StatModifierTarget.HealthRegen, published.Value.Target);
    }

    [TestMethod]
    public void Update_ModifierNotYetExpired_DoesNotPublishEvent()
    {
        var pool = CreatePool();
        pool.Add(0, Modifier(5));
        var eventBus = new EventBus();
        var system = new StatModifierExpirySystem(pool, CreateTiersPool(), new ProcessingTierEvents(), eventBus);
        var publishedCount = 0;
        eventBus.Subscribe<StatModifierExpiredEvent>(_ => publishedCount++);

        system.Update(default, 0);

        Assert.AreEqual(0, publishedCount);
    }
}
