using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.Modules.Core.Systems;

namespace Tests.Modules.Core.Systems;

[TestClass]
public sealed class ActionLockSystemTests
{
    private static PackedComponentPool<ActionLockComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_DecrementsLockFramesRemainingByStripeCount()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        var system = new ActionLockSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(15, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtExactlyStripeCount_DecrementsToZero()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 10, lockFramesRemaining: 10));
        var system = new ActionLockSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_BelowStripeCount_ClampsToZeroInsteadOfGoingNegative()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 4, lockFramesRemaining: 4));
        var system = new ActionLockSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtZero_LeavesUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        var system = new ActionLockSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    /// <summary>Regression guard: Update must never touch TotalLockFrames -- only ActionLockGate.Lock resets it, on the next real action.</summary>
    [TestMethod]
    public void Update_DoesNotChangeTotalLockFrames()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        var system = new ActionLockSystem(pool);

        system.Update(default, 0);

        Assert.AreEqual(25, pool.GetReadonly(0).TotalLockFrames);
    }

    /// <summary>
    /// Regression test: PackedComponentPool.TryUpdate bumps its component's version
    /// unconditionally once its delegate runs, so an already-unlocked entity must never reach
    /// TryUpdate at all, or its version would climb every stripe cycle despite never changing.
    /// </summary>
    [TestMethod]
    public void Update_AtZero_DoesNotBumpVersion()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        var system = new ActionLockSystem(pool);
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }
}
