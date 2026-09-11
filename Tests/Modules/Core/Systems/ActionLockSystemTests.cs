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

    /// <summary>Seeds entity 0's tier explicitly -- an entity with no ProcessingTierComponent resolves to Beyond, not Local (see ProcessingTierWiring), so leaving it absent put entity 0 in a StripeCount 10 * 8 = 80 bucket while these tests read as if it were Local.</summary>
    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool(ProcessingTierLevel tier = ProcessingTierLevel.Local)
    {
        var pool = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);
        pool.Add(0, new ProcessingTierComponent(tier));
        return pool;
    }

    [TestMethod]
    public void Update_DecrementsLockFramesRemainingByStripeCount()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 25, currentLockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(15, pool.GetReadonly(0).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtExactlyStripeCount_DecrementsToZero()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 10, currentLockFramesRemaining: 10));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Update_BelowStripeCount_ClampsToZeroInsteadOfGoingNegative()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 4, currentLockFramesRemaining: 4));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Update_AtZero_LeavesUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentLockFramesRemaining);
    }

    /// <summary>Regression guard: Update must never touch CurrentLockTotalFrames -- only ActionLockGate.Lock resets it, on the next real action.</summary>
    [TestMethod]
    public void Update_DoesNotChangeTotalLockFrames()
    {
        var pool = CreatePool();
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 25, currentLockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(25, pool.GetReadonly(0).CurrentLockTotalFrames);
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
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        var system = new ActionLockSystem(pool, CreateTiersPool(), new ProcessingTierEvents());
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }

    /// <summary>Frames-per-visit for a Neighborhood-tiered entity of the given system -- derived from ProcessingTierDivisors rather than hardcoded, so re-tuning the divisors doesn't silently invalidate these tests' arithmetic (it did once already).</summary>
    private static int NeighborhoodFramesPerVisit(ISystem system) =>
        system.StripeCount * ProcessingTierDivisors.ByTierIndex[(int)ProcessingTierLevel.Neighborhood];

    /// <summary>
    /// A Neighborhood-tiered entity lands in bucket entityId % (StripeCount * divisor) -- for
    /// entity 0, that's bucket 0, due only when FrameCount is a multiple of that product.
    /// The tier must be seeded into the pool before construction, since TieredEntityStripeSet
    /// reads an entity's current tier at membership-add time (during the constructor).
    /// </summary>
    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotDecrement()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool(ProcessingTierLevel.Neighborhood);
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 25, currentLockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, tiers, new ProcessingTierEvents());

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(25, pool.GetReadonly(0).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Decrements()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool(ProcessingTierLevel.Neighborhood);
        pool.Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 25, currentLockFramesRemaining: 25));
        var system = new ActionLockSystem(pool, tiers, new ProcessingTierEvents());
        var framesPerVisit = NeighborhoodFramesPerVisit(system);

        system.Update(new EngineTime(default, default, false, FrameCount: framesPerVisit), 0);

        // Burns off the whole span it was absent for, not the base StripeCount -- a Neighborhood
        // entity is only visited every StripeCount * divisor frames. See ActionLockSystem.Update's
        // own remarks. Clamped at 0 once that span exceeds the lock itself.
        Assert.AreEqual(System.Math.Max(0, 25 - framesPerVisit), pool.GetReadonly(0).CurrentLockFramesRemaining);
    }
}
