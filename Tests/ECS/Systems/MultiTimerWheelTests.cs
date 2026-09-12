using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class MultiTimerWheelTests
{
    private struct Exposure(int effect, uint nextTickFrame) : IKeyedScheduledTimer
    {
        public int Effect { get; } = effect;
        public uint NextTickFrame { get; set; } = nextTickFrame;
        public uint TimerWheelMark { get; set; }
        public readonly int TimerKey => Effect;
    }

    private static MultiComponentPool<Exposure> CreatePool() => new(maximumEntityCount: 16, initialCapacity: 4);

    private static List<(long Frame, int EntityId, int Effect)> Run(MultiTimerWheel<Exposure> wheel, long from, long to, Func<int, Exposure, bool>? remove = null)
    {
        var fires = new List<(long, int, int)>();
        for (var frame = from; frame <= to; frame++)
        {
            wheel.Tick(frame, (entityId, timer, now) =>
            {
                fires.Add((now, entityId, timer.Effect));
                return remove?.Invoke(entityId, timer) ?? true;
            });
        }

        return fires;
    }

    /// <summary>The case membership events can't see: a second instance on an entity that already has one raises no EntityAdded -- and must still be scheduled.</summary>
    [TestMethod]
    public void SecondInstanceOnTheSameEntity_IsScheduled()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        var entityAdded = 0;
        pool.EntityAdded += _ => entityAdded++;

        pool.Add(2, new Exposure(effect: 1, nextTickFrame: 5));
        pool.Add(2, new Exposure(effect: 7, nextTickFrame: 9));

        Assert.AreEqual(1, entityAdded, "Only the first instance is an EntityAdded.");
        CollectionAssert.AreEqual(new[] { (5L, 2, 1), (9L, 2, 7) }, Run(wheel, 0, 20));
    }

    /// <summary>Firing and removing one instance leaves its sibling alone -- the entry names (entity, key), not a dense index that removal would move.</summary>
    [TestMethod]
    public void RemovingOneInstance_LeavesItsSiblingScheduled()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        pool.Add(2, new Exposure(7, 9));
        pool.Add(3, new Exposure(1, 6));

        var fires = Run(wheel, 0, 20);

        CollectionAssert.AreEqual(new[] { (5L, 2, 1), (6L, 3, 1), (9L, 2, 7) }, fires);
        Assert.AreEqual(0, pool.Count);
    }

    [TestMethod]
    public void ReArmViaTryUpdateFirst_FiresAgainAtTheNewFrame()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        var fires = new List<long>();

        for (long frame = 0; frame <= 30; frame++)
        {
            wheel.Tick(frame, (entityId, timer, now) =>
            {
                fires.Add(now);
                pool.TryUpdateFirst(entityId, (timer.Effect, now),
                    static (ref readonly Exposure e, (int Effect, long Now) s) => e.Effect == s.Effect,
                    static (ref Exposure e, (int Effect, long Now) s) => e.NextTickFrame = FrameDeadline.After(s.Now, 12));
                return false;
            });
        }

        CollectionAssert.AreEqual(new long[] { 5, 17, 29 }, fires);
    }

    [TestMethod]
    public void InstanceRemovedBeforeItsDeadline_NeverFires_SiblingStillDoes()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        pool.Add(2, new Exposure(7, 9));

        pool.RemoveFirst(2, static (ref readonly Exposure e) => e.Effect == 1);

        CollectionAssert.AreEqual(new[] { (9L, 2, 7) }, Run(wheel, 0, 20));
    }

    [TestMethod]
    public void InstancesAlreadyInThePool_AreScheduledOnConstruction()
    {
        var pool = CreatePool();
        pool.Add(2, new Exposure(1, 5));
        pool.Add(2, new Exposure(7, 3));

        var wheel = new MultiTimerWheel<Exposure>(pool);

        CollectionAssert.AreEqual(new[] { (3L, 2, 7), (5L, 2, 1) }, Run(wheel, 0, 20));
    }

    [TestMethod]
    public void NeverDeadline_IsParked()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);

        pool.Add(2, new Exposure(effect: 1, nextTickFrame: FrameDeadline.Never));

        Assert.AreEqual(0, wheel.PendingCount);
        Assert.IsEmpty(Run(wheel, 0, 100));
    }

    /// <summary>A callback that neither removes nor re-arms leaves that instance resting -- and its sibling's own schedule untouched.</summary>
    [TestMethod]
    public void NotReArmedNorRemoved_RestsUntilANewDeadlineIsWritten()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        var fires = new List<long>();

        for (long frame = 0; frame <= 30; frame++)
        {
            if (frame == 20)
            {
                pool.TryUpdateFirst(2,
                    static (ref readonly Exposure e) => e.Effect == 1,
                    static (ref Exposure e) => e.NextTickFrame = 25);
            }

            wheel.Tick(frame, (_, _, now) => { fires.Add(now); return false; });
        }

        CollectionAssert.AreEqual(new long[] { 5, 25 }, fires);
        Assert.AreEqual(1, pool.Count);
    }

    /// <summary>A resting instance whose deadline is written again with the same value must still schedule: it's due, and nothing holds an entry for it any more.</summary>
    [TestMethod]
    public void RestingInstance_RewrittenWithItsOldDeadline_FiresAgain()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        var fires = new List<long>();

        for (long frame = 0; frame <= 12; frame++)
        {
            if (frame == 10)
            {
                pool.TryUpdateFirst(2,
                    static (ref readonly Exposure e) => e.Effect == 1,
                    static (ref Exposure e) => e.NextTickFrame = 5);
            }

            wheel.Tick(frame, (_, _, now) => { fires.Add(now); return false; });
        }

        CollectionAssert.AreEqual(new long[] { 5, 10 }, fires, "Rewritten on frame 10 with a deadline already past: fires on the next drain.");
    }

    /// <inheritdoc cref="PackedTimerWheelTests.ReArmedToTheDeadlineThatJustFired_IsSweptAgainNextFrame"/>
    [TestMethod]
    public void ReArmedToTheDeadlineThatJustFired_IsSweptAgainNextFrame()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));
        var fires = new List<long>();

        for (long frame = 0; frame <= 10; frame++)
        {
            wheel.Tick(frame, (entityId, timer, now) =>
            {
                fires.Add(now);
                if (fires.Count >= 3)
                {
                    return true;
                }

                pool.TryUpdateFirst(entityId, timer.Effect,
                    static (ref readonly Exposure e, int effect) => e.Effect == effect,
                    static (ref Exposure e, int effect) => e.NextTickFrame = 5);
                return false;
            });
        }

        CollectionAssert.AreEqual(new long[] { 5, 6, 7 }, fires, "A deadline still behind the clock is swept once per frame -- late, never lost.");
        Assert.AreEqual(0, pool.Count);
    }

    /// <summary>Removals are deferred past the whole frame, so one removal can't disturb validation of the rest -- including two instances on one entity.</summary>
    [TestMethod]
    public void SeveralInstancesDueOnOneFrame_AllFire_ThenRemovalsApply()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 6));
        pool.Add(2, new Exposure(7, 6));
        pool.Add(3, new Exposure(1, 6));

        var fires = Run(wheel, 0, 10);

        CollectionAssert.AreEqual(new[] { (6L, 2, 1), (6L, 2, 7), (6L, 3, 1) }, fires);
        Assert.AreEqual(0, pool.Count);
    }

    /// <summary>The same (entity, key) removed and re-created before its deadline: the stale entry is dropped, only the new instance's own deadline fires.</summary>
    [TestMethod]
    public void RecycledInstance_OnlyTheNewTimerFires()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 5));

        pool.RemoveFirst(2, static (ref readonly Exposure e) => e.Effect == 1);
        pool.Add(2, new Exposure(1, 9));

        CollectionAssert.AreEqual(new[] { (9L, 2, 1) }, Run(wheel, 0, 20));
    }

    /// <summary>
    /// Add puts a new instance at the *head* of the entity's chain, so an instance re-created during
    /// a drain is exactly what a by-key lookup finds afterwards. The deferred removal must not take
    /// it: only the instance still carrying the spent deadline is the one that fired.
    /// </summary>
    [TestMethod]
    public void InstanceReCreatedLaterInTheSameDrain_SurvivesThatFramesRemoval()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 6));
        pool.Add(3, new Exposure(1, 6));
        var fires = new List<(long, int)>();

        for (long frame = 0; frame <= 20; frame++)
        {
            wheel.Tick(frame, (entityId, _, now) =>
            {
                fires.Add((now, entityId));
                if (entityId == 3)
                {
                    // Entity 2's instance already fired this frame and asked to be removed.
                    pool.RemoveFirst(2, static (ref readonly Exposure e) => e.Effect == 1);
                    pool.Add(2, new Exposure(1, 15));
                }

                return true;
            });
        }

        CollectionAssert.AreEqual(new[] { (6L, 2), (6L, 3), (15L, 2) }, fires);
        Assert.AreEqual(0, pool.Count, "Frame 15's own firing removed what was left.");
    }

    /// <summary>The raw-ref contract the observer depends on: a GetByDenseIndex write is seen only once IncrementVersionByDenseIndex reports it.</summary>
    [TestMethod]
    public void RawRefWrite_IsScheduledOnceItsVersionIsIncremented()
    {
        var pool = CreatePool();
        var wheel = new MultiTimerWheel<Exposure>(pool);
        pool.Add(2, new Exposure(1, 50));
        var denseIndex = pool.GetFirstDenseIndex(2);

        pool.GetByDenseIndex(denseIndex).NextTickFrame = 7;
        pool.IncrementVersionByDenseIndex(denseIndex);

        CollectionAssert.AreEqual(new[] { (7L, 2, 1) }, Run(wheel, 0, 60));
    }

    /// <inheritdoc cref="PackedTimerWheelTests.SecondWheelOverTheSamePool_Throws"/>
    [TestMethod]
    public void SecondWheelOverTheSamePool_Throws()
    {
        var pool = CreatePool();
        _ = new MultiTimerWheel<Exposure>(pool);

        Assert.ThrowsExactly<InvalidOperationException>(() => new MultiTimerWheel<Exposure>(pool));
    }
}
