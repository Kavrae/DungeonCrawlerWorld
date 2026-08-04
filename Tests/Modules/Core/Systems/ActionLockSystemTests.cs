using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Core.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.Core.Systems;

[TestClass]
public sealed class ActionLockSystemTests
{
    private static PackedComponentPool<ActionLockComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_DecrementsLockFramesRemainingByStripeCount()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(15, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtExactlyStripeCount_DecrementsToZero()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 10, lockFramesRemaining: 10));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_BelowStripeCount_ClampsToZeroInsteadOfGoingNegative()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 4, lockFramesRemaining: 4));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtZero_LeavesUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).LockFramesRemaining);
    }

    /// <summary>Regression guard: Update must never touch TotalLockFrames -- only ActionLockGate.Lock resets it, on the next real action.</summary>
    [TestMethod]
    public void Update_DoesNotChangeTotalLockFrames()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

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
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }

    /// <summary>
    /// A Neighborhood-tiered entity (StripeCount 10 * divisor 2 = 20) lands in bucket
    /// entityId % 20 -- for entity 0, that's bucket 0, due only when FrameCount % 20 == 0.
    /// The tier must be seeded into the pool before construction, since TieredEntityStripeSet
    /// reads an entity's current tier at membership-add time (during the constructor).
    /// </summary>
    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrement()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new ActionLockSystem(pool, tiers, new ProcessingTierEvents());

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(25, pool.GetReadonly(0).LockFramesRemaining);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Decrements()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new ActionLockComponent(totalLockFrames: 25, lockFramesRemaining: 25));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new ActionLockSystem(pool, tiers, new ProcessingTierEvents());

        system.Update(new EngineTime(default, default, false, FrameCount: 20), 0);

        Assert.AreEqual(15, pool.GetReadonly(0).LockFramesRemaining);
    }
}
