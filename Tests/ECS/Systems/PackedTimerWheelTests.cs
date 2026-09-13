using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;

namespace Tests.ECS.Systems;

[TestClass]
public sealed class PackedTimerWheelTests
{
    private struct Burn : IScheduledTimer
    {
        public uint NextTickFrame { get; set; }
        public uint TimerWheelMark { get; set; }
        public int Stacks { get; set; }
    }

    private const int Period = 10;

    private static PackedComponentPool<Burn> CreatePool() =>
        new(maximumEntityCount: 16, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Runs frames from..to inclusive, recording (frame, entity) for every fire. The callback re-arms by Period until Stacks runs out, then asks for removal -- BurningSystem's shape.</summary>
    private static List<(long Frame, int EntityId)> Run(PackedTimerWheel<Burn> wheel, PackedComponentPool<Burn> pool, long from, long to)
    {
        var fires = new List<(long, int)>();
        for (var frame = from; frame <= to; frame++)
        {
            wheel.Tick(frame, (entityId, timer, now) =>
            {
                fires.Add((now, entityId));
                if (timer.Stacks <= 1)
                {
                    return true;
                }

                pool.TryUpdate(entityId, now, static (ref Burn b, long n) =>
                {
                    b.Stacks--;
                    b.NextTickFrame = FrameDeadline.After(n, Period);
                });
                return false;
            });
        }

        return fires;
    }

    [TestMethod]
    public void AddedTimer_IsScheduledWithoutAnyoneTellingTheWheel()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);

        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 1 });

        CollectionAssert.AreEqual(new[] { (5L, 3) }, Run(wheel, pool, 0, 30));
        Assert.IsFalse(pool.Has(3), "Callback returned true: removed after firing.");
    }

    [TestMethod]
    public void ReArmedInsideTheCallback_FiresEveryPeriodUntilRemoved()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 3 });

        CollectionAssert.AreEqual(new[] { (5L, 3), (15L, 3), (25L, 3) }, Run(wheel, pool, 0, 60));
    }

    /// <summary>A write that leaves the deadline alone (a stack top-off) must not schedule a duplicate -- the reason TimerWheelMark exists.</summary>
    [TestMethod]
    public void WriteThatKeepsTheDeadline_DoesNotDoubleFire()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 1 });

        pool.TryUpdate(3, static (ref Burn b) => b.Stacks = 2);
        pool.TryUpdate(3, static (ref Burn b) => b.Stacks = 2);

        Assert.AreEqual(1, wheel.PendingCount);
        CollectionAssert.AreEqual(new[] { (5L, 3), (15L, 3) }, Run(wheel, pool, 0, 30));
    }

    /// <summary>Merge replaces the whole struct (TimerWheelMark included) -- even with the same deadline it must still fire exactly once.</summary>
    [TestMethod]
    public void MergeReplacingWithTheSameDeadline_FiresOnce()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 1 });

        pool.Merge(3, new Burn { NextTickFrame = 5, Stacks = 1 });

        CollectionAssert.AreEqual(new[] { (5L, 3) }, Run(wheel, pool, 0, 30));
    }

    /// <summary>The automatic-scheduling payoff: moving a deadline *earlier* from anywhere fires at the new frame, and not again at the old one.</summary>
    [TestMethod]
    public void DeadlineMovedEarlier_FiresAtTheNewFrameOnly()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 20, Stacks = 1 });

        pool.TryUpdate(3, static (ref Burn b) => b.NextTickFrame = 8);

        CollectionAssert.AreEqual(new[] { (8L, 3) }, Run(wheel, pool, 0, 40));
    }

    [TestMethod]
    public void DeadlineMovedLater_FiresAtTheNewFrameOnly()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 8, Stacks = 1 });

        pool.TryUpdate(3, static (ref Burn b) => b.NextTickFrame = 20);

        CollectionAssert.AreEqual(new[] { (20L, 3) }, Run(wheel, pool, 0, 40));
    }

    [TestMethod]
    public void RemovedBeforeItsDeadline_NeverFires()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 8, Stacks = 1 });

        pool.Remove(3);

        Assert.IsEmpty(Run(wheel, pool, 0, 40));
    }

    /// <summary>EntityManager recycles ids: a new timer on the same id must not inherit the old one's pending fire.</summary>
    [TestMethod]
    public void RecycledEntityId_OnlyTheNewTimerFires()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 8, Stacks = 1 });
        pool.Remove(3);

        pool.Add(3, new Burn { NextTickFrame = 12, Stacks = 1 });

        CollectionAssert.AreEqual(new[] { (12L, 3) }, Run(wheel, pool, 0, 40));
    }

    [TestMethod]
    public void TimersAlreadyInThePool_AreScheduledOnConstruction()
    {
        var pool = CreatePool();
        pool.Add(1, new Burn { NextTickFrame = 4, Stacks = 1 });
        pool.Add(2, new Burn { NextTickFrame = 6, Stacks = 1 });

        var wheel = new PackedTimerWheel<Burn>(pool);

        CollectionAssert.AreEqual(new[] { (4L, 1), (6L, 2) }, Run(wheel, pool, 0, 20));
    }

    /// <summary>A callback that neither removes nor re-arms leaves the timer resting -- it fires once, then waits for a new deadline to be written, which schedules it again.</summary>
    [TestMethod]
    public void NotReArmedNorRemoved_RestsUntilANewDeadlineIsWritten()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5 });
        var fires = new List<long>();

        for (long frame = 0; frame <= 30; frame++)
        {
            if (frame == 20)
            {
                pool.TryUpdate(3, static (ref Burn b) => b.NextTickFrame = 25);
            }

            wheel.Tick(frame, (_, _, now) => { fires.Add(now); return false; });
        }

        CollectionAssert.AreEqual(new long[] { 5, 25 }, fires);
        Assert.IsTrue(pool.Has(3));
    }

    /// <summary>
    /// A resting timer's deadline written again with the *same* value -- e.g. a merge that happens to
    /// match -- must still schedule it: it's due, and nothing holds an entry for it any more.
    /// </summary>
    [TestMethod]
    public void RestingTimer_RewrittenWithItsOldDeadline_FiresAgain()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5 });
        var fires = new List<long>();

        for (long frame = 0; frame <= 12; frame++)
        {
            if (frame == 10)
            {
                pool.Merge(3, new Burn { NextTickFrame = 5 });
            }

            wheel.Tick(frame, (_, _, now) => { fires.Add(now); return false; });
        }

        CollectionAssert.AreEqual(new long[] { 5, 10 }, fires, "Rewritten on frame 10 with a deadline already past: fires on the next drain.");
    }

    [TestMethod]
    public void NeverDeadline_IsParked()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);

        pool.Add(3, new Burn { NextTickFrame = FrameDeadline.Never, Stacks = 1 });

        Assert.AreEqual(0, wheel.PendingCount);
        Assert.IsEmpty(Run(wheel, pool, 0, 100));
    }

    /// <summary>The raw-ref contract the observer depends on: a GetByDenseIndex write is seen only once IncrementVersionByDenseIndex reports it.</summary>
    [TestMethod]
    public void RawRefWrite_IsScheduledOnceItsVersionIsIncremented()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 50, Stacks = 1 });
        var denseIndex = pool.GetDenseIndex(3);

        pool.GetByDenseIndex(denseIndex).NextTickFrame = 7;
        pool.IncrementVersionByDenseIndex(denseIndex);

        CollectionAssert.AreEqual(new[] { (7L, 3) }, Run(wheel, pool, 0, 60));
    }

    /// <summary>Removals are deferred past the whole frame, so one removal can't disturb validation of the rest -- two timers due together both fire.</summary>
    [TestMethod]
    public void SeveralDueOnOneFrame_AllFire_ThenRemovalsApply()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        for (var entityId = 1; entityId <= 4; entityId++)
        {
            pool.Add(entityId, new Burn { NextTickFrame = 6, Stacks = 1 });
        }

        var fires = Run(wheel, pool, 0, 10);

        CollectionAssert.AreEqual(new[] { (6L, 1), (6L, 2), (6L, 3), (6L, 4) }, fires);
        Assert.AreEqual(0, pool.Count);
    }

    /// <summary>
    /// Re-arming to the very deadline that just fired is a legitimate "sweep me again" -- a next
    /// deadline recomputed from state the callback itself just changed lands on it easily. The mark
    /// is released before the callback runs precisely so this works: writing the same value back is
    /// a write like any other and schedules the timer afresh, instead of reading as the firing the
    /// wheel had already delivered and leaving a live deadline nothing would ever fire.
    /// </summary>
    [TestMethod]
    public void ReArmedToTheDeadlineThatJustFired_IsSweptAgainNextFrame()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 3 });
        var fires = new List<long>();

        for (long frame = 0; frame <= 12; frame++)
        {
            wheel.Tick(frame, (entityId, timer, now) =>
            {
                fires.Add(now);
                if (timer.Stacks <= 1)
                {
                    return true;
                }

                pool.TryUpdate(entityId, static (ref Burn b) =>
                {
                    b.Stacks--;
                    b.NextTickFrame = 5;
                });
                return false;
            });
        }

        CollectionAssert.AreEqual(new long[] { 5, 6, 7 }, fires, "A deadline still behind the clock is swept once per frame -- late, never lost.");
        Assert.IsFalse(pool.Has(3));
    }

    /// <summary>
    /// Duplicate entries for one deadline are routine -- a whole-struct MergeAction clears the mark,
    /// so re-merging an unchanged deadline schedules a second entry for it. Firing one must not
    /// arm the other, even when the callback re-arms to exactly that deadline: nothing re-claims a
    /// mark during the firing pass, so the spent duplicate finds a released mark and is dropped.
    /// </summary>
    [TestMethod]
    public void DuplicateEntriesForOneDeadline_FireOnce_EvenWhenTheCallbackReArmsToIt()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(3, new Burn { NextTickFrame = 5, Stacks = 1 });
        pool.Merge(3, new Burn { NextTickFrame = 5, Stacks = 1 });
        Assert.AreEqual(2, wheel.PendingCount, "The whole-struct merge cleared the mark: two entries for frame 5.");

        var fires = new List<long>();
        for (long frame = 0; frame <= 10; frame++)
        {
            wheel.Tick(frame, (entityId, _, now) =>
            {
                fires.Add(now);
                if (fires.Count == 1)
                {
                    pool.TryUpdate(entityId, static (ref Burn b) => b.NextTickFrame = 5);
                }

                return false;
            });
        }

        CollectionAssert.AreEqual(new long[] { 5, 6 }, fires, "Once on frame 5 despite two entries, then once more for the re-arm.");
    }

    /// <summary>
    /// A removal asked for early in a drain must not take a timer something re-armed later in that
    /// same drain: the entity id alone doesn't identify the timer that fired, the spent deadline does.
    /// </summary>
    [TestMethod]
    public void ReArmedByAnotherTimerLaterInTheSameDrain_SurvivesThatFramesRemoval()
    {
        var pool = CreatePool();
        var wheel = new PackedTimerWheel<Burn>(pool);
        pool.Add(2, new Burn { NextTickFrame = 6, Stacks = 1 });
        pool.Add(1, new Burn { NextTickFrame = 6, Stacks = 1 });
        var fires = new List<(long, int)>();

        for (long frame = 0; frame <= 20; frame++)
        {
            wheel.Tick(frame, (entityId, _, now) =>
            {
                fires.Add((now, entityId));
                if (entityId == 1)
                {
                    // Entity 2 already fired this frame and asked to be removed -- re-igniting it now
                    // is the case the flush has to notice.
                    pool.TryUpdate(2, static (ref Burn b) => b.NextTickFrame = 14);
                }

                return true;
            });
        }

        CollectionAssert.AreEqual(new[] { (6L, 2), (6L, 1), (14L, 2) }, fires);
        Assert.AreEqual(0, pool.Count, "Frame 14's own firing removed what was left.");
    }

    /// <summary>Scheduling state lives on the component, so a second wheel would silently schedule nothing. The pool refuses it outright.</summary>
    [TestMethod]
    public void SecondWheelOverTheSamePool_Throws()
    {
        var pool = CreatePool();
        _ = new PackedTimerWheel<Burn>(pool);

        Assert.ThrowsExactly<InvalidOperationException>(() => new PackedTimerWheel<Burn>(pool));
    }
}
