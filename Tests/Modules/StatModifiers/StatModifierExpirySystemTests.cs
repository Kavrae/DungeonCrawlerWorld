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

    private static MultiComponentPool<ExpiringStatModifierComponent> CreateMarkersPool() => new(maximumEntityCount: 10, initialCapacity: 4);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    private static StatModifierComponent Modifier(ushort? remainingDurationFrames) =>
        new(StatModifierTarget.HealthRegen, StatModifierOperation.Additive, StatModifierPolarity.Buff, canModify: false, magnitude: 1f, remainingDurationFrames, StatusEffectSource.Admin);

    [TestMethod]
    public void Update_DecrementsRemainingDurationFramesByOne()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        pool.Add(0, Modifier(5));
        markers.Add(0, new ExpiringStatModifierComponent());
        var system = new StatModifierExpirySystem(pool, markers, CreateTiersPool(), new ProcessingTierEvents(), new EventBus());

        system.Update(default, 0);

        Assert.AreEqual((ushort?)4, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_AtZero_RemovesModifier()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        pool.Add(0, Modifier(0));
        markers.Add(0, new ExpiringStatModifierComponent());
        var system = new StatModifierExpirySystem(pool, markers, CreateTiersPool(), new ProcessingTierEvents(), new EventBus());

        system.Update(default, 0);

        Assert.AreEqual(-1, pool.GetFirstDenseIndex(0));
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrement()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        var tiers = CreateTiersPool();
        pool.Add(0, Modifier(5));
        markers.Add(0, new ExpiringStatModifierComponent());
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new StatModifierExpirySystem(pool, markers, tiers, new ProcessingTierEvents(), new EventBus());

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual((ushort?)5, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Decrements()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        var tiers = CreateTiersPool();
        pool.Add(0, Modifier(5));
        markers.Add(0, new ExpiringStatModifierComponent());
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new StatModifierExpirySystem(pool, markers, tiers, new ProcessingTierEvents(), new EventBus());

        system.Update(new EngineTime(default, default, false, FrameCount: 2), 0);

        Assert.AreEqual((ushort?)4, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
    }

    [TestMethod]
    public void Update_ModifierExpires_PublishesStatModifierExpiredEventWithEntityIdAndTarget()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        pool.Add(0, Modifier(1));
        markers.Add(0, new ExpiringStatModifierComponent());
        var eventBus = new EventBus();
        var system = new StatModifierExpirySystem(pool, markers, CreateTiersPool(), new ProcessingTierEvents(), eventBus);
        StatModifierExpiredEvent? published = null;
        eventBus.Subscribe<StatModifierExpiredEvent>(evt => published = evt);

        system.Update(default, 0);

        Assert.IsNotNull(published);
        Assert.AreEqual(0, published.Value.EntityId);
        Assert.AreEqual(StatModifierTarget.HealthRegen, published.Value.Target);

        Assert.AreEqual(-1, markers.GetFirstDenseIndex(0));
    }

    [TestMethod]
    public void Update_ModifierNotYetExpired_DoesNotPublishEvent()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        pool.Add(0, Modifier(5));
        markers.Add(0, new ExpiringStatModifierComponent());
        var eventBus = new EventBus();
        var system = new StatModifierExpirySystem(pool, markers, CreateTiersPool(), new ProcessingTierEvents(), eventBus);
        var publishedCount = 0;
        eventBus.Subscribe<StatModifierExpiredEvent>(_ => publishedCount++);

        system.Update(default, 0);

        Assert.AreEqual(0, publishedCount);
    }

    /// <summary>
    /// Membership in the driving pool (markers), not presence in the raw storage pool, is what
    /// gates visitation -- the actual behavior StatModifierExpirySystem's fix depends on. Entity
    /// 0 has a modifier in `pool` one decrement away from expiring (which WOULD decrement,
    /// remove, and publish an event if the entity were visited), but no marker, simulating
    /// exactly what StatModifierEffects.Apply produces for a permanent grant (a real
    /// StatModifierComponent, no ExpiringStatModifierComponent). If StatModifierExpirySystem
    /// were still driven off `pool` instead of `markers`, this test would fail.
    /// </summary>
    [TestMethod]
    public void Update_EntityHasNoMarker_IsNeverVisitedEvenWithAnExpiringModifierInStorage()
    {
        var pool = CreatePool();
        var markers = CreateMarkersPool();
        pool.Add(0, Modifier(1));
        var eventBus = new EventBus();
        var system = new StatModifierExpirySystem(pool, markers, CreateTiersPool(), new ProcessingTierEvents(), eventBus);
        var publishedCount = 0;
        eventBus.Subscribe<StatModifierExpiredEvent>(_ => publishedCount++);

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual((ushort?)1, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(0)).RemainingDurationFrames);
        Assert.AreEqual(0, publishedCount);
    }
}
