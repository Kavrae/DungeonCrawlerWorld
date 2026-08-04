using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests.Modules.ContactDamage;

[TestClass]
public sealed class ContactDamageSystemTests
{
    private const int TerrainEntityId = 100;
    private const int MoverEntityId = 0;

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    /// <summary>Minimal IMapQuery test double -- only GetTerrainEntityIdAt is exercised by ContactDamageSystem, everything else is a fixed/empty answer.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int X, int Y, int Z), int> _terrainByPosition = [];

        public Vector3Int MapSize { get; } = new(1000, 1000, 3);
        public bool IsOnMap(Vector3Int position) => true;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public bool IsBlocking(int entityId) => true;

        public void SetTerrain(Vector3Int position, int entityId) => _terrainByPosition[(position.X, position.Y, position.Z)] = entityId;

        public int GetTerrainEntityIdAt(Vector3Int position) =>
            _terrainByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);

    }

    private static PackedComponentPool<DamageOnContactComponent> CreateHazardPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<ContactDamageExposureComponent> CreateExposurePool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<HealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool() =>
        new(initialCapacity: 200, static (ref existing, incoming) => existing = incoming);

    private static (
        ContactDamageSystem System,
        PackedComponentPool<DamageOnContactComponent> Hazards,
        PackedComponentPool<ContactDamageExposureComponent> Exposures,
        PackedComponentPool<HealthComponent> Health,
        FakeMapQuery MapQuery,
        FrameEventBuffer<EntityMoved> MovedEntities,
        PackedComponentPool<DeadComponent> DeadEntities,
        DirectComponentPool<ProcessingTierComponent> ProcessingTiers) Build()
    {
        var hazards = CreateHazardPool();
        var exposures = CreateExposurePool();
        var health = CreateHealthPool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMoved>();
        var deadEntities = CreateDeadPool();
        var processingTiers = CreateTiersPool();

        health.Add(MoverEntityId, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        hazards.Add(TerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        mapQuery.SetTerrain(new Vector3Int(5, 5, 0), TerrainEntityId);

        var system = new ContactDamageSystem(hazards, exposures, health, new EventBus(), mapQuery, new FakePlayerQuery(MoverEntityId), movedEntities, processingTiers, new ProcessingTierEvents(), statModifiers: null, deadEntities: deadEntities);

        return (system, hazards, exposures, health, mapQuery, movedEntities, deadEntities, processingTiers);
    }

    /// <summary>
    /// One real frame: Update, then clear the buffer -- the second half is normally
    /// SystemManager's job (see FrameEventBuffer's own doc comment), done here explicitly since
    /// these tests construct ContactDamageSystem directly, bypassing SystemManager entirely.
    /// Without this, a recorded move would still be sitting in the buffer on every subsequent
    /// loop iteration, getting silently reprocessed (re-adding the exposure, re-dealing contact
    /// damage) every single call instead of just once.
    /// </summary>
    private static void SimulateFrame(ContactDamageSystem system, FrameEventBuffer<EntityMoved> movedEntities, byte stripeIndex = 0)
    {
        system.Update(default, stripeIndex);
        movedEntities.ClearFrame();
    }

    [TestMethod]
    public void SteppingOntoHazard_DealsImmediateDamage()
    {
        var (system, _, _, health, _, movedEntities, _, _) = Build();

        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities);

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>
    /// Detecting the move and ticking existing exposures both now happen inside the same
    /// Update call (draining the moved-entities buffer, then CountdownTicker.Tick) -- so a
    /// freshly-added exposure is also ticked once within that same call, landing at 59, not the
    /// full 60. This actually matches real gameplay more accurately than the old EventBus-based
    /// version's "60 immediately after publish" ever did: MovementSystem published EntityMoved
    /// synchronously mid-Update, before ContactDamageSystem's own registered Update (and its
    /// tick pass) ran later that same frame -- so the exposure was already ticked once by the
    /// end of that real frame too, just via a separate call the old isolated-publish-then-assert
    /// test never actually exercised together.
    /// </summary>
    [TestMethod]
    public void SteppingOntoHazard_AddsExposureWithCountdownAlreadyTickedOnceThisFrame()
    {
        var (system, _, exposures, _, _, movedEntities, _, _) = Build();

        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities);

        Assert.IsTrue(exposures.Has(MoverEntityId));
        Assert.AreEqual(59, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }

    [TestMethod]
    public void SteppingOntoNonHazardTile_GrantsNoExposure()
    {
        var (system, _, exposures, health, _, movedEntities, _, _) = Build();

        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(4, 6, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities);

        Assert.IsFalse(exposures.Has(MoverEntityId));
        Assert.AreEqual(100, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_DealsDamageAgainAfterSixtyFrames()
    {
        var (system, _, _, health, _, movedEntities, _, _) = Build();
        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        // The first of these 60 frames both drains the buffer (adding the exposure and dealing
        // the initial hit) and runs the first tick-decrement, so the total tick count across
        // this loop is unchanged from the old publish-then-60-updates version.
        for (var frame = 0; frame < 60; frame++)
        {
            SimulateFrame(system, movedEntities);
        }

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_FiftyNineFrames_DoesNotDealDamageYet()
    {
        var (system, _, _, health, _, movedEntities, _, _) = Build();
        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        for (var frame = 0; frame < 59; frame++)
        {
            SimulateFrame(system, movedEntities);
        }

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DeadEntityAlreadyExposed_DoesNotTakeFurtherDamage()
    {
        var (system, _, _, health, _, movedEntities, deadEntities, _) = Build();
        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities); // Onto the hazard: exposure added, immediate 10 damage -> 90.
        deadEntities.Add(MoverEntityId, new DeadComponent(KilledByEntityId: null));

        for (var frame = 0; frame < 60; frame++)
        {
            SimulateFrame(system, movedEntities);
        }

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth, "A corpse standing in lava must not keep taking contact damage forever.");
    }

    [TestMethod]
    public void SteppingOffHazard_StopsFurtherDamage()
    {
        var (system, _, exposures, health, _, movedEntities, _, _) = Build();
        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(5, 5, 0), new Vector3Int(6, 5, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities); // Drains both buffered moves: onto the hazard (adds exposure + damage), then off it (removes the exposure) -- all before this call's own tick pass.

        Assert.IsFalse(exposures.Has(MoverEntityId));

        for (var frame = 0; frame < 120; frame++)
        {
            SimulateFrame(system, movedEntities);
        }

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void HazardToHazardMove_RetriggersImmediateDamageAndResetsCountdown()
    {
        var (system, hazards, exposures, health, mapQuery, movedEntities, _, _) = Build();
        const int secondTerrainEntityId = 101;
        hazards.Add(secondTerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        mapQuery.SetTerrain(new Vector3Int(6, 5, 0), secondTerrainEntityId);

        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        for (var frame = 0; frame < 30; frame++)
        {
            SimulateFrame(system, movedEntities);
        }

        movedEntities.Record(new EntityMoved(MoverEntityId, new Vector3Int(5, 5, 0), new Vector3Int(6, 5, 0), new Vector2Byte(1, 1)));
        SimulateFrame(system, movedEntities); // Drains the second move (retrigger + reset to 60) then ticks it once in this same call, landing at 59 -- see SteppingOntoHazard_AddsExposureWithCountdownAlreadyTickedOnceThisFrame's own doc comment for why.

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(59, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }

    /// <summary>Only the periodic re-check pass (CountdownTicker.Tick) is ProcessingTier-gated, not the buffer drain -- see Update's own comment. Sets up an existing exposure directly (bypassing the move-based grant) to exercise that pass in isolation.</summary>
    [TestMethod]
    public void Update_ThrottledMover_OffCycle_DoesNotDecrementExposureCountdown()
    {
        var (system, _, exposures, _, _, _, _, processingTiers) = Build();
        processingTiers.Add(MoverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        exposures.Add(MoverEntityId, new ContactDamageExposureComponent(60, TerrainEntityId));

        // MoverEntityId (0), Neighborhood-tiered (base StripeCount 1 * divisor 2 = 2), lands in bucket 0 -- due only when FrameCount % 2 == 0.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(60, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_ThrottledMover_OnEligibleCycle_DecrementsExposureCountdown()
    {
        var (system, _, exposures, _, _, _, _, processingTiers) = Build();
        processingTiers.Add(MoverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        exposures.Add(MoverEntityId, new ContactDamageExposureComponent(60, TerrainEntityId));

        system.Update(new EngineTime(default, default, false, FrameCount: 2), 0);

        // Decremented by the Neighborhood tier's own framesPerVisit (base StripeCount 1 * divisor 2 = 2).
        Assert.AreEqual(58, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }
}
