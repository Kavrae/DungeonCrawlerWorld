using Engine.Bootstrap;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Energy.Components;
using Game.Modules.Energy.Systems;
using Game.Modules.Health;
using Game.Modules.Movement;
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
    /// Deep correctness check against the real EnergyRechargeSystem: a population that
    /// doesn't divide evenly by StripeCount (23 entities, 10 stripes) must still have every
    /// entity touched exactly once per full cycle, with each individual frame touching only
    /// ceil(23/10)=3 or floor(23/10)=2 entities -- never the whole population at once.
    /// </summary>
    [TestMethod]
    public void EnergyRechargeSystem_OverOneFullCycle_TouchesEveryEntityExactlyOnceWithBoundedPerFrameWork()
    {
        const int entityCount = 23;
        var pool = new PackedComponentPool<EnergyComponent>(entityCount, entityCount,
            static (ref EnergyComponent existing, EnergyComponent incoming) => existing = incoming);

        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            pool.Add(entityId, new EnergyComponent(currentEnergy: 0, energyRecharge: 1, maximumEnergy: 1000));
        }

        var system = new EnergyRechargeSystem(pool);
        var touchCountByEntityId = new int[entityCount];
        var previousEnergy = new short[entityCount];

        for (var frame = 0; frame < system.StripeCount; frame++)
        {
            system.Update(default, (byte)frame);

            var touchedThisFrame = 0;
            for (var entityId = 0; entityId < entityCount; entityId++)
            {
                var currentEnergy = pool.GetReadonly(entityId).CurrentEnergy;
                if (currentEnergy != previousEnergy[entityId])
                {
                    touchCountByEntityId[entityId]++;
                    touchedThisFrame++;
                    previousEnergy[entityId] = currentEnergy;
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
    /// double-process. Entities 9 and 19 are both in stripe 9 (entityId % 10); entity 9 is
    /// removed after stripes 0-8 have already fired this cycle but before stripe 9 fires.
    /// EntityStripeSet buckets by entityId, so removing 9 can only ever affect stripe 9's own
    /// bucket -- entity 19 must still be touched exactly once when stripe 9 fires, not zero
    /// and not twice, and entities 0-8 (in unrelated stripes) must be completely unaffected.
    /// </summary>
    [TestMethod]
    public void EnergyRechargeSystem_EntityRemovedMidCycle_DoesNotCorruptOtherStripes()
    {
        var pool = new PackedComponentPool<EnergyComponent>(30, 30,
            static (ref EnergyComponent existing, EnergyComponent incoming) => existing = incoming);

        for (var entityId = 0; entityId < 10; entityId++)
        {
            pool.Add(entityId, new EnergyComponent(currentEnergy: 0, energyRecharge: 1, maximumEnergy: 1000));
        }
        pool.Add(19, new EnergyComponent(currentEnergy: 0, energyRecharge: 1, maximumEnergy: 1000)); // Stripe 9, alongside entity 9.

        var system = new EnergyRechargeSystem(pool);
        var touchCountByEntityId = new Dictionary<int, int>();
        var previousEnergy = new Dictionary<int, short> { [19] = 0 };
        for (var entityId = 0; entityId < 10; entityId++)
        {
            previousEnergy[entityId] = 0;
        }

        void RecordTouches()
        {
            foreach (var entityId in previousEnergy.Keys.ToArray())
            {
                if (!pool.Has(entityId))
                {
                    continue;
                }

                var currentEnergy = pool.GetReadonly(entityId).CurrentEnergy;
                if (currentEnergy != previousEnergy[entityId])
                {
                    touchCountByEntityId[entityId] = touchCountByEntityId.GetValueOrDefault(entityId) + 1;
                    previousEnergy[entityId] = currentEnergy;
                }
            }
        }

        for (byte stripe = 0; stripe < 9; stripe++)
        {
            system.Update(default, stripe);
            RecordTouches();
        }

        // Entity 9 has already had its stripe (9) skipped so far this cycle -- it hasn't
        // fired yet. Remove it now, before stripe 9 runs.
        pool.Remove(9);

        system.Update(default, stripeIndex: 9);
        RecordTouches();

        Assert.IsFalse(pool.Has(9));
        Assert.AreEqual(1, touchCountByEntityId.GetValueOrDefault(19), "Entity 19 (same stripe as the removed entity) must be touched exactly once, not skipped or double-processed.");
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

        var movementModule = new MovementModule();
        movementModule.Configure(new GameModuleContext(world, mathUtility, new EventBus()));

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            movementModule,
        ];

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 500, initialComponentCapacity: 500);
        var energyPool = ecsContext.ComponentManager.GetPackedPool<EnergyComponent>();

        const int entityCount = 200;
        for (var x = 0; x < entityCount; x++)
        {
            var entityId = ecsContext.EntityManager.CreateEntity();
            var transform = new TransformComponent(new Vector3Int(x % 20, x / 20, 0), new Vector2Byte(1, 1));
            ecsContext.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, transform);
            world.PlaceEntityOnMap(entityId, transform.Position, ref transform);

            energyPool.Add(entityId, new EnergyComponent(currentEnergy: 0, energyRecharge: 5, maximumEnergy: 1000));
            ecsContext.ComponentManager.GetPackedPool<Game.Modules.Health.Components.HealthComponent>().Add(entityId, new Game.Modules.Health.Components.HealthComponent(100, 10, 100));

            // Deliberately no MovementComponent: MovementSystem consumes energy on a
            // successful move, which would make "was this entity recharged" a noisy signal
            // to assert on below. MovementModule stays registered (realism, and to prove the
            // three real systems still coexist without throwing) but with an empty population.
        }

        for (var frame = 0; frame < 60; frame++)
        {
            ecsContext.Update(default);
        }

        var rechargedCount = 0;
        for (var entityId = 0; entityId < entityCount; entityId++)
        {
            if (energyPool.GetReadonly(entityId).CurrentEnergy > 0)
            {
                rechargedCount++;
            }
        }

        // 60 frames is 6 full EnergyRechargeSystem cycles (StripeCount 10) -- every entity
        // should have been touched at least once by now.
        Assert.AreEqual(entityCount, rechargedCount);
    }
}
