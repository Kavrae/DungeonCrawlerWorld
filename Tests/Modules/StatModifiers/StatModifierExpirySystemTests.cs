using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Utilities;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatModifiers.Systems;
using Game.World;

namespace Tests.Modules.StatModifiers;

[TestClass]
public sealed class StatModifierExpirySystemTests
{
    private const int EntityId = 0;

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static MultiComponentPool<StatModifierComponent> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    /// <summary>Mirrors StatModifiersModule's own registration -- merging keeps the earlier deadline, which is what makes a newly granted modifier pull the entity's pending expiry forward.</summary>
    private static PackedComponentPool<ExpiringStatModifierComponent> CreateExpiriesPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) =>
            existing.NextTickFrame = System.Math.Min(existing.NextTickFrame, incoming.NextTickFrame));

    private static StatModifierComponent Modifier(uint expiresAtFrame, StatModifierTarget target = StatModifierTarget.HealthRegen) =>
        new(target, StatModifierOperation.Additive, StatModifierPolarity.Buff, canModify: false, magnitude: 1f, expiresAtFrame, StatusEffectSource.Admin);

    /// <summary>Runs every frame from..to inclusive -- the way SystemManager drives it.</summary>
    private static void Run(StatModifierExpirySystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    [TestMethod]
    public void BeforeItsExpiryFrame_ModifierStays()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 60));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Run(system, 0, 59);

        Assert.AreEqual(1, pool.CountForEntity(EntityId));
    }

    [TestMethod]
    public void OnItsExpiryFrame_ModifierIsRemoved()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 60));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Run(system, 0, 60);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
        Assert.IsFalse(expiries.Has(EntityId), "Nothing expiring is left, so the entity's own expiry timer goes too.");
    }

    /// <summary>
    /// A modifier granted after the system was built schedules itself: the system watches the
    /// modifier pool's own change notification, so no grant path has to remember to register an
    /// expiry (this is what the fieldless marker StatModifierEffects.Apply used to add by hand
    /// -- and what the action-effect grant path never added at all).
    /// </summary>
    [TestMethod]
    public void ModifierGrantedAfterConstruction_IsScheduledAutomatically()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());
        Run(system, 0, 100);

        pool.Add(EntityId, Modifier(expiresAtFrame: 160));
        Assert.IsTrue(expiries.Has(EntityId));

        Run(system, 101, 159);
        Assert.AreEqual(1, pool.CountForEntity(EntityId));

        system.Update(Frame(160), 0);
        Assert.AreEqual(0, pool.CountForEntity(EntityId));
    }

    /// <summary>The other half of the same guarantee -- modifiers already in the pool when the system is constructed (a blueprint-built entity) are scheduled too, not just ones granted later.</summary>
    [TestMethod]
    public void ModifierGrantedBeforeConstruction_IsScheduledAutomatically()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 5));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Run(system, 0, 5);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
    }

    [TestMethod]
    public void PermanentModifier_IsNeverScheduledAndNeverRemoved()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(FrameDeadline.Never));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Assert.IsFalse(expiries.Has(EntityId), "A permanent modifier costs no timer at all.");

        Run(system, 0, 200);

        Assert.AreEqual(1, pool.CountForEntity(EntityId));
    }

    /// <summary>One timer per entity, not per modifier: the entity's expiry timer follows the earliest deadline, then re-arms to the next one rather than dropping the modifiers behind it.</summary>
    [TestMethod]
    public void SeveralModifiers_EachRemovedOnItsOwnFrame()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 30, StatModifierTarget.HealthRegen));
        pool.Add(EntityId, Modifier(expiresAtFrame: 90, StatModifierTarget.OutgoingDamage));
        pool.Add(EntityId, Modifier(FrameDeadline.Never, StatModifierTarget.MaximumHealth));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Run(system, 0, 30);
        Assert.AreEqual(2, pool.CountForEntity(EntityId), "Only the 30-frame one is gone.");
        Assert.IsTrue(expiries.Has(EntityId), "Re-armed for the 90-frame one.");
        Assert.AreEqual(90u, expiries.GetReadonly(EntityId).NextTickFrame);

        Run(system, 31, 90);
        Assert.AreEqual(1, pool.CountForEntity(EntityId), "The permanent one stays.");
        Assert.IsFalse(expiries.Has(EntityId));
    }

    /// <summary>
    /// Two buffs on the SAME stat, one second apart -- the case sharing a single per-entity timer
    /// could plausibly get wrong by expiring everything at the earliest deadline. The 5s buff must
    /// go on its own frame and leave the 6s one running for its last second.
    /// </summary>
    [TestMethod]
    public void TwoBuffsOnTheSameStat_TheShorterExpiresWithoutTakingTheLongerWithIt()
    {
        const uint FiveSeconds = 5 * GameTiming.FramesPerSecond;
        const uint SixSeconds = 6 * GameTiming.FramesPerSecond;

        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(SixSeconds, StatModifierTarget.Constitution));
        pool.Add(EntityId, Modifier(FiveSeconds, StatModifierTarget.Constitution));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        Run(system, 0, FiveSeconds);

        Assert.AreEqual(1, pool.CountForEntity(EntityId), "The 5s buff expired; the 6s buff must not have gone with it.");
        Assert.AreEqual(SixSeconds, pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(EntityId)).ExpiresAtFrame, "The survivor is the 6s one.");
        Assert.AreEqual(SixSeconds, expiries.GetReadonly(EntityId).NextTickFrame, "The entity's timer re-armed to the survivor's own deadline.");

        Run(system, FiveSeconds + 1, SixSeconds - 1);
        Assert.AreEqual(1, pool.CountForEntity(EntityId), "Still buffed through its last second.");

        system.Update(Frame(SixSeconds), 0);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
        Assert.IsFalse(expiries.Has(EntityId));
    }

    /// <summary>A later grant must not push the pending expiry out, and an earlier one must pull it in -- the pool's merge policy, exercised through a real grant.</summary>
    [TestMethod]
    public void GrantingALaterModifier_DoesNotDelayThePendingExpiry()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 50, StatModifierTarget.HealthRegen));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());

        pool.Add(EntityId, Modifier(expiresAtFrame: 500, StatModifierTarget.OutgoingDamage));
        Assert.AreEqual(50u, expiries.GetReadonly(EntityId).NextTickFrame);

        Run(system, 0, 50);

        Assert.AreEqual(1, pool.CountForEntity(EntityId));
        Assert.AreEqual(500u, expiries.GetReadonly(EntityId).NextTickFrame);
    }

    [TestMethod]
    public void ModifierExpires_PublishesStatModifierExpiredEventWithEntityIdAndTarget()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 1));
        var eventBus = new EventBus();
        StatModifierExpiredEvent? published = null;
        eventBus.Subscribe<StatModifierExpiredEvent>(evt => published = evt);
        var system = new StatModifierExpirySystem(pool, expiries, eventBus);

        Run(system, 0, 1);

        Assert.IsNotNull(published);
        Assert.AreEqual(EntityId, published.Value.EntityId);
        Assert.AreEqual(StatModifierTarget.HealthRegen, published.Value.Target);
    }

    [TestMethod]
    public void ModifierNotYetExpired_DoesNotPublishEvent()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 50));
        var eventBus = new EventBus();
        var publishedCount = 0;
        eventBus.Subscribe<StatModifierExpiredEvent>(_ => publishedCount++);
        var system = new StatModifierExpirySystem(pool, expiries, eventBus);

        Run(system, 0, 49);

        Assert.AreEqual(0, publishedCount);
    }

    /// <summary>A subscriber granting a fresh modifier while handling the expiry must not have its grant dropped -- the next deadline is computed after the events are published, not before.</summary>
    [TestMethod]
    public void ModifierGrantedByAnExpiredEventSubscriber_IsStillScheduled()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 10));
        var eventBus = new EventBus();
        var granted = false;
        eventBus.Subscribe<StatModifierExpiredEvent>(evt =>
        {
            if (granted)
            {
                return;
            }

            granted = true;
            pool.Add(evt.EntityId, Modifier(expiresAtFrame: 40, StatModifierTarget.OutgoingDamage));
        });
        var system = new StatModifierExpirySystem(pool, expiries, eventBus);

        Run(system, 0, 10);
        Assert.AreEqual(1, pool.CountForEntity(EntityId));
        Assert.IsTrue(expiries.Has(EntityId), "The replacement modifier's own expiry must still be scheduled.");

        Run(system, 11, 40);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
    }

    /// <summary>
    /// The awkward case that shook out of the test above: a subscriber granting a modifier whose
    /// deadline has already been reached by the frame handling the expiry. Re-arming the entity's
    /// timer to the deadline that just fired would read to the wheel as the firing it already
    /// delivered, leaving a live modifier nothing would ever remove -- it has to be swept on the
    /// next frame instead.
    /// </summary>
    [TestMethod]
    public void ModifierGrantedWithAnAlreadyReachedDeadline_IsRemovedOnTheNextFrame()
    {
        var pool = CreatePool();
        var expiries = CreateExpiriesPool();
        pool.Add(EntityId, Modifier(expiresAtFrame: 10));
        var eventBus = new EventBus();
        var granted = false;
        eventBus.Subscribe<StatModifierExpiredEvent>(evt =>
        {
            if (granted)
            {
                return;
            }

            granted = true;
            pool.Add(evt.EntityId, Modifier(expiresAtFrame: 10, StatModifierTarget.OutgoingDamage));
        });
        var system = new StatModifierExpirySystem(pool, expiries, eventBus);

        Run(system, 0, 10);
        Assert.AreEqual(1, pool.CountForEntity(EntityId), "The stale grant landed after this frame's sweep had already passed it.");

        system.Update(Frame(11), 0);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
        Assert.IsFalse(expiries.Has(EntityId));
    }

    [TestMethod]
    public void NoModifiers_DoesNotThrow()
    {
        var system = new StatModifierExpirySystem(CreatePool(), CreateExpiriesPool(), new EventBus());

        system.Update(Frame(1), 0);
    }
}
