using Engine.ECS.Entities;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.Movement;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.World;

namespace Tests.Modules;

/// <summary>
/// Backs the Phase 5 entity-striping implementation (plan decision #11, reversed from the
/// cheaper per-period offset fix once level 2's population was confirmed to grow
/// exponentially -- see the plan). SystemManager no longer schedules by period/offset at
/// all, so the old same-frame-collision concern (SystemSchedulingCollisionTests) is moot by
/// construction; what actually needs proving now is that striping itself is correct: bounded
/// per-frame work and full population coverage over one cycle.
/// </summary>
[TestClass]
public sealed class EntityStripingTests
{
    /// <summary>
    /// Deep correctness check against the real SimpleHealthRegenSystem: a population that
    /// doesn't divide evenly by StripeCount (133 entities, 60 stripes -- StripeCount is a full
    /// second's worth of frames, see SimpleHealthRegenSystem's own doc comment) must still have every
    /// entity touched exactly once per full cycle, with each individual frame touching at most
    /// one round of whole id blocks (EntityStripeSet.BlockSize each) -- never the whole population at once.
    /// </summary>
    [TestMethod]
    public void HealthRegenSystem_OverOneFullCycle_TouchesEveryEntityExactlyOnceWithBoundedPerFrameWork()
    {
        const int entityCount = 133;
        var pool = new PackedComponentPool<SimpleHealthComponent>(entityCount, entityCount,
            static (ref existing, incoming) => existing = incoming);

        var abilityScores = AbilityScoreTestPools.CreatePool(entityCount, entityCount);
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: entityCount, static (ref existing, incoming) => existing = incoming);
        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            pool.Add(entityId, new SimpleHealthComponent(currentHealth: 0, maximumHealth: 1000));
            // Constitution total 300 -- SimpleHealthRegenSystem's MaxHealthRegenPerSecond -- so every
            // visit actually changes CurrentHealth (a nonzero, easily-detected "touch"), same
            // role the old flat healthRegen:1 constructor argument used to play.
            abilityScores.Add(entityId, AbilityScoreTestPools.Score(AbilityScoreType.Constitution, baseValue: 300, total: 300));
            // Pinned to Local -- this test is about striping/cycle-coverage correctness
            // (StripeCount frames == one full cycle), not tier throttling, so it shouldn't
            // depend on whatever the untiered fail-open default happens to be.
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }

        var system = TestSystems.SimpleHealthRegenSystem(pool, processingTiers, new ProcessingTierEvents(), abilityScores: abilityScores);
        var touchCountByEntityId = new int[entityCount];
        var previousHealth = new float[entityCount];
        var blockCount = (entityCount + EntityStripeSet.BlockSize - 1) / EntityStripeSet.BlockSize;
        var maximumPerFrame = (blockCount + system.StripeCount - 1) / system.StripeCount * EntityStripeSet.BlockSize;

        for (var frame = 0; frame < system.StripeCount; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)frame);

            var touchedThisFrame = 0;
            for (var entityId = 0; entityId < entityCount; entityId++)
            {
                var currentHealth = pool.GetReadonly(entityId).CurrentHealth;
                if (currentHealth != previousHealth[entityId])
                {
                    touchCountByEntityId[entityId]++;
                    touchedThisFrame++;
                    previousHealth[entityId] = currentHealth;
                }
            }

            Assert.IsTrue(touchedThisFrame <= maximumPerFrame, $"Frame {frame} touched {touchedThisFrame} entities; expected at most {maximumPerFrame} (whole id blocks, dealt round-robin).");
        }

        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            Assert.AreEqual(1, touchCountByEntityId[entityId], $"Entity {entityId} should be touched exactly once per full stripe cycle.");
        }
    }

    /// <summary>
    /// Regression test for the exact bug scenario discussed alongside decision #11: removing
    /// an entity from a not-yet-visited stripe mid-cycle used to be able (under the old
    /// dense-index striding) to relocate a different, already-visited entity into that
    /// stripe's index range via PackedComponentPool.Remove's swap-with-last, causing a
    /// double-process. The removed entity (block 9) and its partner (block 69) are both in stripe 9
    /// (block % 60 -- StripeCount is 60, see SimpleHealthRegenSystem's own doc comment); the removed
    /// one goes after stripes 0-8 have already fired this cycle but before stripe 9 fires.
    /// EntityStripeSet buckets by entity id, so the removal can only ever affect stripe 9's own
    /// bucket -- the partner must still be touched exactly once when stripe 9 fires, not zero and
    /// not twice, and the entities in blocks 0-8 (unrelated stripes) must be completely unaffected.
    /// </summary>
    [TestMethod]
    public void HealthRegenSystem_EntityRemovedMidCycle_DoesNotCorruptOtherStripes()
    {
        const int B = EntityStripeSet.BlockSize;
        const int removedEntityId = 9 * B;
        const int partnerEntityId = 69 * B;
        const int capacity = partnerEntityId + 1;
        var pool = new PackedComponentPool<SimpleHealthComponent>(capacity, capacity,
            static (ref existing, incoming) => existing = incoming);

        var abilityScores = AbilityScoreTestPools.CreatePool(capacity, capacity);
        // Pinned to Local for every entity below -- see the previous test's own comment on why.
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: capacity, static (ref existing, incoming) => existing = incoming);
        var unrelatedEntityIds = Enumerable.Range(0, 9).Select(block => block * B).ToArray();
        foreach (var entityId in unrelatedEntityIds.Append(removedEntityId).Append(partnerEntityId))
        {
            pool.Add(entityId, new SimpleHealthComponent(currentHealth: 0, maximumHealth: 1000));
            abilityScores.Add(entityId, AbilityScoreTestPools.Score(AbilityScoreType.Constitution, baseValue: 300, total: 300));
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }

        var system = TestSystems.SimpleHealthRegenSystem(pool, processingTiers, new ProcessingTierEvents(), abilityScores: abilityScores);
        var touchCountByEntityId = new Dictionary<int, int>();
        var previousHealth = unrelatedEntityIds.Append(removedEntityId).Append(partnerEntityId).ToDictionary(entityId => entityId, _ => 0f);

        void RecordTouches()
        {
            foreach (var entityId in previousHealth.Keys.ToArray())
            {
                if (!pool.Has(entityId))
                {
                    continue;
                }

                var currentHealth = pool.GetReadonly(entityId).CurrentHealth;
                if (currentHealth != previousHealth[entityId])
                {
                    touchCountByEntityId[entityId] = touchCountByEntityId.GetValueOrDefault(entityId) + 1;
                    previousHealth[entityId] = currentHealth;
                }
            }
        }

        for (byte stripe = 0; stripe < 9; stripe++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: stripe), stripe);
            RecordTouches();
        }

        // The removed entity's stripe (9) hasn't fired yet this cycle. Remove it now, before stripe 9 runs.
        pool.Remove(removedEntityId);

        system.Update(new EngineTime(default, default, false, FrameCount: 9), stripeIndex: 9);
        RecordTouches();

        Assert.IsFalse(pool.Has(removedEntityId));
        Assert.AreEqual(1, touchCountByEntityId.GetValueOrDefault(partnerEntityId), "The partner (same stripe as the removed entity) must be touched exactly once, not skipped or double-processed.");
        foreach (var entityId in unrelatedEntityIds)
        {
            Assert.AreEqual(1, touchCountByEntityId.GetValueOrDefault(entityId), $"Entity {entityId} in an unrelated stripe must be unaffected by the removal.");
        }
    }

    /// <summary>
    /// Integration sanity with a larger population than any single test elsewhere uses,
    /// specifically to catch striping-related edge cases (off-by-one bucket bounds, index
    /// shifts from pool churn) that a single-entity test wouldn't exercise. Runs long enough
    /// to cover several full cycles of both the period-10 and period-15 systems.
    /// </summary>
    [TestMethod]
    public void RealSystemsWithLargePopulation_RunManyFrames_DoesNotThrowAndKeepsRecharging()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 1)), initialEntityCapacity: 500, initialComponentCapacity: 500);
        var world = pass.World;
        var ecsContext = pass.EcsContext;
        var healthPool = ecsContext.ComponentManager.GetPackedPool<SimpleHealthComponent>();

        const int entityCount = 200;
        for (var x = 0; x < entityCount; x++)
        {
            var entityId = ecsContext.EntityManager.CreateEntity();
            var transform = new TransformComponent(new Vector3Int(x % 20, x / 20, 0), new Vector2Byte(1, 1));
            ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
            world.PlaceEntityOnMap(entityId, transform.Position, ref transform);

            // Pinned to Local, and added *before* SimpleHealthComponent below -- there's no player
            // entity/IPlayerQuery registered in this test, so the real ProcessingTierSystem
            // never actually computes a tier for anyone (its own Update no-ops without a
            // player). This test is about striping/cycle-coverage correctness across real
            // systems, not tier throttling, so it shouldn't depend on whatever the untiered
            // fail-open default happens to be. Must come before healthPool.Add: SimpleHealthRegenSystem's
            // TieredEntityStripeSet is driven off the health pool's EntityAdded event, which
            // looks up this entity's tier at the moment it fires -- adding the
            // ProcessingTierComponent afterward wouldn't retroactively fix its bucket, since
            // nothing in this test ever raises TierChanged to migrate it later.
            ecsContext.ComponentManager.GetDirectPool<ProcessingTierComponent>().Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

            // Starts at 0, not MaximumHealth, so "CurrentHealth > 0" below actually proves
            // SimpleHealthRegenSystem touched this entity rather than being vacuously true from
            // the start. No MovementComponent: keeps this test minimal (MovementSystem
            // doesn't touch SimpleHealthComponent, so there's no cross-contamination to avoid --
            // MovementModule stays registered purely to prove the two real systems still
            // coexist without throwing, with an empty movement population).
            healthPool.Add(entityId, new SimpleHealthComponent(currentHealth: 0, maximumHealth: 1000));
            AbilityScoreEffects.Grant(ecsContext.ComponentManager, entityId, AbilityScoreType.Constitution, baseValue: 300);
        }

        for (var frame = 0; frame < 60; frame++)
        {
            ecsContext.Update(new EngineTime(default, default, false, FrameCount: frame));
        }

        var rechargedCount = 0;
        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            if (healthPool.GetReadonly(entityId).CurrentHealth > 0)
            {
                rechargedCount++;
            }
        }

        // 60 frames is exactly 1 full SimpleHealthRegenSystem cycle now (StripeCount is a full
        // second's worth of frames -- see SimpleHealthRegenSystem's own doc comment) -- every entity
        // should have been touched at least once by now.
        Assert.AreEqual(entityCount, rechargedCount);
    }
}