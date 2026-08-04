using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.Health;

[TestClass]
public sealed class HealthRegenSystemTests
{
    private static PackedComponentPool<HealthComponent> CreatePool() =>
        new(maximumEntityCount: 10, initialCapacity: 4,
            static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 10,
            static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_RegeneratesHealthByHealthRegenAmount()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 10, maximumHealth: 200));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ClampsAtMaximumHealth()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 195, healthRegen: 10, maximumHealth: 200));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(200, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotRegenerate()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 0, healthRegen: 10, maximumHealth: 200));
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities);

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ZeroRegen_LeavesCurrentHealthUnchanged()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 200));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    /// <summary>
    /// Regression test: PackedComponentPool.TryUpdate bumps its component's version
    /// unconditionally once its delegate runs, so a zero-regen entity must never reach
    /// TryUpdate at all, or its version would climb every stripe cycle despite never changing.
    /// </summary>
    [TestMethod]
    public void Update_ZeroRegen_DoesNotBumpVersion()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 0, maximumHealth: 200));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());
        var versionBeforeUpdate = pool.GetVersion(0);

        system.Update(default, 0);

        Assert.AreEqual(versionBeforeUpdate, pool.GetVersion(0));
    }

    /// <summary>
    /// Regression test: CurrentHealth += HealthRegen used to compute in short and could
    /// silently overflow/underflow before the subsequent clamp ran. A large negative regen
    /// against a very negative CurrentHealth underflows short's range and wraps to a large
    /// positive number -- if that wrapped value were what got clamped, it would land near
    /// MaximumHealth instead of the mathematically correct 0.
    /// </summary>
    [TestMethod]
    public void Update_LargeNegativeRegen_ClampsToZeroInsteadOfUnderflowWrapping()
    {
        var pool = CreatePool();
        pool.Add(0, new HealthComponent(currentHealth: -32000, healthRegen: -1000, maximumHealth: 200));
        var system = new HealthRegenSystem(pool, CreateTiersPool(), new ProcessingTierEvents());

        system.Update(default, 0);

        Assert.AreEqual(0, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotRegenerate()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 10, maximumHealth: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new HealthRegenSystem(pool, tiers, new ProcessingTierEvents());

        // Entity 0, Neighborhood-tiered (StripeCount 10 * divisor 2 = 20), lands in bucket 0 -- due only when FrameCount % 20 == 0.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(50, pool.GetReadonly(0).CurrentHealth);
    }

    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Regenerates()
    {
        var pool = CreatePool();
        var tiers = CreateTiersPool();
        pool.Add(0, new HealthComponent(currentHealth: 50, healthRegen: 10, maximumHealth: 200));
        tiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        var system = new HealthRegenSystem(pool, tiers, new ProcessingTierEvents());

        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.AreEqual(60, pool.GetReadonly(0).CurrentHealth);
    }
}