using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;

namespace Tests.Modules.Core.Components;

[TestClass]
public sealed class ActionLockGateTests
{
    private static PackedComponentPool<ActionLockComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void IsBlocked_MissingComponent_ReturnsTrue()
    {
        var pool = CreatePool();

        Assert.IsTrue(ActionLockGate.IsBlocked(pool, 0));
    }

    [TestMethod]
    public void IsBlocked_LockFramesRemainingPositive_ReturnsTrue()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 5, lockFramesRemaining: 5));

        Assert.IsTrue(ActionLockGate.IsBlocked(pool, 0));
    }

    [TestMethod]
    public void IsBlocked_LockFramesRemainingZero_ReturnsFalse()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 5, lockFramesRemaining: 0));

        Assert.IsFalse(ActionLockGate.IsBlocked(pool, 0));
    }

    [TestMethod]
    public void Lock_ExistingComponent_SetsLockFramesRemaining()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        ActionLockGate.Lock(pool, 0, 42);

        Assert.AreEqual(42, pool.GetReadonly(0).LockFramesRemaining);
    }

    /// <summary>Lock sets both fields to the same value -- a fresh action always resets the "how much of the lock is left, as a fraction of the whole" denominator too, not just the countdown.</summary>
    [TestMethod]
    public void Lock_ExistingComponent_AlsoSetsTotalLockFrames()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 10, lockFramesRemaining: 0));

        ActionLockGate.Lock(pool, 0, 42);

        Assert.AreEqual(42, pool.GetReadonly(0).TotalLockFrames);
    }

    [TestMethod]
    public void Lock_MissingComponent_DoesNotThrow() =>
        ActionLockGate.Lock(CreatePool(), 0, 42);
}
