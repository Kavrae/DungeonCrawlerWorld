using Engine.Bootstrap;
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
    /// Deep correctness check against the real HealthRegenSystem: a population that
    /// doesn't divide evenly by StripeCount (133 entities, 60 stripes -- StripeCount is a full
    /// second's worth of frames, see HealthRegenSystem's own doc comment) must still have every
    /// entity touched exactly once per full cycle, with each individual frame touching only
    /// ceil(133/60)=3 or floor(133/60)=2 entities -- never the whole population at once.
    /// </summary>
    [TestMethod]
    public void HealthRegenSystem_OverOneFullCycle_TouchesEveryEntityExactlyOnceWithBoundedPerFrameWork()
    {
        const int entityCount = 133;
        var pool = new PackedComponentPool<HealthComponent>(entityCount, entityCount,
            static (ref existing, incoming) => existing = incoming);

        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(entityCount, entityCount);
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: entityCount, static (ref existing, incoming) => existing = incoming);
        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            pool.Add(entityId, new HealthComponent(currentHealth: 0, maximumHealth: 1000));
            // Constitution total 300 -- HealthRegenSystem's MaxHealthRegenPerSecond -- so every
            // visit actually changes CurrentHealth (a nonzero, easily-detected "touch"), same
            // role the old flat healthRegen:1 constructor argument used to play.
            abilityScores.Add(entityId, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
            // Pinned to Local -- this test is about striping/cycle-coverage correctness
            // (StripeCount frames == one full cycle), not tier throttling, so it shouldn't
            // depend on whatever the untiered fail-open default happens to be.
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }

        var system = new HealthRegenSystem(pool, processingTiers, new ProcessingTierEvents(), abilityScores: abilityScores);
        var touchCountByEntityId = new int[entityCount];
        var previousHealth = new float[entityCount];

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

            Assert.IsTrue(touchedThisFrame is 2 or 3, $"Frame {frame} touched {touchedThisFrame} entities; expected 2 or 3 (bounded to Count/StripeCount).");
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
    /// double-process. Entities 9 and 69 are both in stripe 9 (entityId % 60 -- StripeCount is
    /// 60, see HealthRegenSystem's own doc comment); entity 9 is removed after stripes 0-8 have
    /// already fired this cycle but before stripe 9 fires. EntityStripeSet buckets by entityId,
    /// so removing 9 can only ever affect stripe 9's own bucket -- entity 69 must still be
    /// touched exactly once when stripe 9 fires, not zero and not twice, and entities 0-8 (in
    /// unrelated stripes) must be completely unaffected.
    /// </summary>
    [TestMethod]
    public void HealthRegenSystem_EntityRemovedMidCycle_DoesNotCorruptOtherStripes()
    {
        var pool = new PackedComponentPool<HealthComponent>(100, 100,
            static (ref existing, incoming) => existing = incoming);

        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(100, 100);
        // Pinned to Local for every entity below -- see the previous test's own comment on why.
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(initialCapacity: 100, static (ref existing, incoming) => existing = incoming);
        for (var entityId = 0; entityId < 10; entityId++)
        {
            pool.Add(entityId, new HealthComponent(currentHealth: 0, maximumHealth: 1000));
            abilityScores.Add(entityId, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }
        pool.Add(69, new HealthComponent(currentHealth: 0, maximumHealth: 1000)); // Stripe 9 (69 % 60), alongside entity 9.
        abilityScores.Add(69, new AbilityScoreComponent(AbilityScoreType.Constitution, baseValue: 300, total: 300));
        processingTiers.Add(69, new ProcessingTierComponent(ProcessingTierLevel.Local));

        var system = new HealthRegenSystem(pool, processingTiers, new ProcessingTierEvents(), abilityScores: abilityScores);
        var touchCountByEntityId = new Dictionary<int, int>();
        var previousHealth = new Dictionary<int, float> { [69] = 0 };
        for (var entityId = 0; entityId < 10; entityId++)
        {
            previousHealth[entityId] = 0;
        }

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

        // Entity 9 has already had its stripe (9) skipped so far this cycle -- it hasn't
        // fired yet. Remove it now, before stripe 9 runs.
        pool.Remove(9);

        system.Update(new EngineTime(default, default, false, FrameCount: 9), stripeIndex: 9);
        RecordTouches();

        Assert.IsFalse(pool.Has(9));
        Assert.AreEqual(1, touchCountByEntityId.GetValueOrDefault(69), "Entity 69 (same stripe as the removed entity) must be touched exactly once, not skipped or double-processed.");
        for (var entityId = 0; entityId < 9; entityId++)
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
        var world = new Game.World.World(new Map(new Vector3Int(20, 20, 1)));
        var mathUtility = new MathUtility();

        var context = new GameModuleContext(world, mathUtility, new EventBus()) { EntityMoveSync = new WorldEventSync(world) };

        var movementModule = new MovementModule();
        movementModule.Configure(context);

        var processingTierModule = new ProcessingTierModule();
        processingTierModule.Configure(context);

        var coreModule = new CoreModule();
        coreModule.Configure(context);

        var healthModule = new HealthModule();
        healthModule.Configure(context);

        var statModifiersModule = new StatModifiersModule();
        statModifiersModule.Configure(context);

        var abilityScoresModule = new AbilityScoresModule();
        abilityScoresModule.Configure(context);

        IReadOnlyList<IModule> modules =
        [
            coreModule,
            healthModule,
            statModifiersModule,
            abilityScoresModule,
            movementModule,
            processingTierModule,
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 500, initialComponentCapacity: 500);
        var healthPool = ecsContext.ComponentManager.GetPackedPool<HealthComponent>();

        const int entityCount = 200;
        for (var x = 0; x < entityCount; x++)
        {
            var entityId = ecsContext.EntityManager.CreateEntity();
            var transform = new TransformComponent(new Vector3Int(x % 20, x / 20, 0), new Vector2Byte(1, 1));
            ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
            world.PlaceEntityOnMap(entityId, transform.Position, ref transform);

            // Pinned to Local, and added *before* HealthComponent below -- there's no player
            // entity/IPlayerQuery registered in this test, so the real ProcessingTierSystem
            // never actually computes a tier for anyone (its own Update no-ops without a
            // player). This test is about striping/cycle-coverage correctness across real
            // systems, not tier throttling, so it shouldn't depend on whatever the untiered
            // fail-open default happens to be. Must come before healthPool.Add: HealthRegenSystem's
            // TieredEntityStripeSet is driven off the health pool's EntityAdded event, which
            // looks up this entity's tier at the moment it fires -- adding the
            // ProcessingTierComponent afterward wouldn't retroactively fix its bucket, since
            // nothing in this test ever raises TierChanged to migrate it later.
            ecsContext.ComponentManager.GetDirectPool<ProcessingTierComponent>().Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

            // Starts at 0, not MaximumHealth, so "CurrentHealth > 0" below actually proves
            // HealthRegenSystem touched this entity rather than being vacuously true from
            // the start. No MovementComponent: keeps this test minimal (MovementSystem
            // doesn't touch HealthComponent, so there's no cross-contamination to avoid --
            // MovementModule stays registered purely to prove the two real systems still
            // coexist without throwing, with an empty movement population).
            healthPool.Add(entityId, new HealthComponent(currentHealth: 0, maximumHealth: 1000));
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

        // 60 frames is exactly 1 full HealthRegenSystem cycle now (StripeCount is a full
        // second's worth of frames -- see HealthRegenSystem's own doc comment) -- every entity
        // should have been touched at least once by now.
        Assert.AreEqual(entityCount, rechargedCount);
    }
}