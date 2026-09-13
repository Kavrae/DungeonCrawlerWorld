using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;

namespace Tests.Modules.Core.Components;

[TestClass]
public sealed class ActionLockGateTests
{
    private static PackedComponentPool<ActionLockComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<ActionLockComponent> PoolLockedUntil(uint unlockedAtFrame, ushort totalFrames = 5)
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: totalFrames, unlockedAtFrame));
        return pool;
    }

    /// <summary>An entity with no lock component at all reads as blocked -- unchanged from when this was a countdown, and what stops a half-built entity from acting.</summary>
    [TestMethod]
    public void IsBlocked_MissingComponent_ReturnsTrue() =>
        Assert.IsTrue(ActionLockGate.IsBlocked(CreatePool(), 0, now: 0));

    [TestMethod]
    public void IsBlocked_BeforeItsUnlockFrame_ReturnsTrue() =>
        Assert.IsTrue(ActionLockGate.IsBlocked(PoolLockedUntil(100), 0, now: 99));

    [TestMethod]
    public void IsBlocked_OnItsUnlockFrame_ReturnsFalse() =>
        Assert.IsFalse(ActionLockGate.IsBlocked(PoolLockedUntil(100), 0, now: 100));

    /// <summary>Nothing ticks the lock down, so a frame long past the deadline has to read as unlocked too -- there is no "stuck at a stale value" state a countdown could leave behind.</summary>
    [TestMethod]
    public void IsBlocked_LongAfterItsUnlockFrame_ReturnsFalse() =>
        Assert.IsFalse(ActionLockGate.IsBlocked(PoolLockedUntil(100), 0, now: 100_000));

    [TestMethod]
    public void IsBlocked_NeverLocked_ReturnsFalse() =>
        Assert.IsFalse(ActionLockGate.IsBlocked(PoolLockedUntil(0, totalFrames: 0), 0, now: 0));

    [TestMethod]
    public void Lock_SetsTheDeadlineRelativeToNow()
    {
        var pool = PoolLockedUntil(0, totalFrames: 0);

        ActionLockGate.Lock(pool, 0, now: 100, framesToWait: 42);

        Assert.AreEqual(142u, pool.GetReadonly(0).UnlockedAtFrame);
        Assert.IsTrue(ActionLockGate.IsBlocked(pool, 0, now: 141));
        Assert.IsFalse(ActionLockGate.IsBlocked(pool, 0, now: 142));
    }

    /// <summary>Lock sets the UI's denominator too -- a fresh action always resets "how much of the lock is left, as a fraction of the whole", not just the deadline.</summary>
    [TestMethod]
    public void Lock_AlsoSetsTotalLockFrames()
    {
        var pool = PoolLockedUntil(0, totalFrames: 10);

        ActionLockGate.Lock(pool, 0, now: 100, framesToWait: 42);

        Assert.AreEqual((ushort)42, pool.GetReadonly(0).CurrentLockTotalFrames);
    }

    [TestMethod]
    public void Lock_NoFramesGiven_UsesTheEntitysOwnStandardLockFrames()
    {
        var pool = PoolLockedUntil(0, totalFrames: 0);

        ActionLockGate.Lock(pool, 0, now: 10);

        Assert.AreEqual(10u + ActionLockGate.StandardLockFrames, pool.GetReadonly(0).UnlockedAtFrame);
    }

    [TestMethod]
    public void Lock_MissingComponent_DoesNotThrow() =>
        ActionLockGate.Lock(CreatePool(), 0, now: 0, framesToWait: 42);

    [TestMethod]
    public void FramesRemaining_CountsDownWithoutAnythingTickingIt()
    {
        var actionLock = PoolLockedUntil(100).GetReadonly(0);

        Assert.AreEqual(100, ActionLockGate.FramesRemaining(actionLock, now: 0));
        Assert.AreEqual(1, ActionLockGate.FramesRemaining(actionLock, now: 99));
        Assert.AreEqual(0, ActionLockGate.FramesRemaining(actionLock, now: 100));
        Assert.AreEqual(0, ActionLockGate.FramesRemaining(actionLock, now: 500), "Never negative.");
    }
}
