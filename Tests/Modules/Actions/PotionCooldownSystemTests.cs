using Engine.ECS.Components.Stores;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Systems;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class PotionCooldownSystemTests
{
    private const int EntityId = 1;

    private static (PotionCooldownSystem System, PackedComponentPool<PotionCooldownComponent> Cooldowns) Build()
    {
        var cooldowns = new PackedComponentPool<PotionCooldownComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        // Every id these tests use, seeded Local -- an entity with no ProcessingTierComponent
        // resolves to Beyond (see ProcessingTierWiring), whose far coarser cadence would put it in
        // a bucket no single-Update test reaches.
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 10, static (ref existing, incoming) => existing = incoming);
        for (var entityId = 0; entityId < 10; entityId++)
        {
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }
        return (new PotionCooldownSystem(cooldowns, processingTiers, new ProcessingTierEvents()), cooldowns);
    }

    /// <summary>EntityId is 1, so it lands in bucket 1 and is only due when FrameCount leaves remainder 1 -- FrameCount 0 (the `default` EngineTime other tests here use) reaches bucket 0 instead. This system was StripeCount 1 and untiered until it turned out to be one of the largest costs in the simulation; see its own doc comment.</summary>
    [TestMethod]
    public void Update_TicksFramesRemainingDownByFramesPerVisit()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1200));

        system.Update(new EngineTime(default, default, false, FrameCount: EntityId), 0);

        Assert.AreEqual(1200 - system.StripeCount, cooldowns.GetReadonly(EntityId).FramesRemaining);
    }

    [TestMethod]
    public void Update_FramesRemainingReachesZero_RemovesTheComponent()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1));

        system.Update(new EngineTime(default, default, false, FrameCount: EntityId), 0);

        Assert.IsFalse(cooldowns.Has(EntityId));
    }

    [TestMethod]
    public void Update_NoEntitiesWithCooldown_DoesNotThrow()
    {
        var (system, _) = Build();

        system.Update(default, 0);
    }

    [TestMethod]
    /// <summary>Entities 1 and 2 land in different stripe buckets now that this system is tiered, so each needs its own due frame -- one Update no longer reaches both. The independence being asserted (each countdown advancing only on its own visits) is unchanged.</summary>
    public void Update_MultipleEntities_EachTickedIndependently()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(1, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 500));
        cooldowns.Add(2, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(500 - system.StripeCount, cooldowns.GetReadonly(1).FramesRemaining);
        Assert.IsTrue(cooldowns.Has(2), "Entity 2 is in a different bucket -- frame 1 is not its due frame.");

        system.Update(new EngineTime(default, default, false, FrameCount: 2), 0);

        Assert.AreEqual(500 - system.StripeCount, cooldowns.GetReadonly(1).FramesRemaining, "Entity 1 must not advance on entity 2's due frame.");
        Assert.IsFalse(cooldowns.Has(2));
    }
}
