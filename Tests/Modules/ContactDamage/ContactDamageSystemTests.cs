using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Health.Components;
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

    private static (
        ContactDamageSystem System,
        PackedComponentPool<DamageOnContactComponent> Hazards,
        PackedComponentPool<ContactDamageExposureComponent> Exposures,
        PackedComponentPool<HealthComponent> Health,
        FakeMapQuery MapQuery,
        EventBus EventBus) Build()
    {
        var hazards = CreateHazardPool();
        var exposures = CreateExposurePool();
        var health = CreateHealthPool();
        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        health.Add(MoverEntityId, new HealthComponent(currentHealth: 100, healthRegen: 0, maximumHealth: 100));
        hazards.Add(TerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        mapQuery.SetTerrain(new Vector3Int(5, 5, 0), TerrainEntityId);

        var system = new ContactDamageSystem(hazards, exposures, health, eventBus, mapQuery, new FakePlayerQuery(MoverEntityId));

        return (system, hazards, exposures, health, mapQuery, eventBus);
    }

    [TestMethod]
    public void SteppingOntoHazard_DealsImmediateDamage()
    {
        var (_, _, _, health, _, eventBus) = Build();

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void SteppingOntoHazard_AddsExposureWithFullCountdown()
    {
        var (_, _, exposures, _, _, eventBus) = Build();

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        Assert.IsTrue(exposures.Has(MoverEntityId));
        Assert.AreEqual(60, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }

    [TestMethod]
    public void SteppingOntoNonHazardTile_GrantsNoExposure()
    {
        var (_, _, exposures, health, _, eventBus) = Build();

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(4, 6, 0), new Vector2Byte(1, 1)));

        Assert.IsFalse(exposures.Has(MoverEntityId));
        Assert.AreEqual(100, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_DealsDamageAgainAfterSixtyFrames()
    {
        var (system, _, _, health, _, eventBus) = Build();
        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        for (var frame = 0; frame < 60; frame++)
        {
            system.Update(default, 0);
        }

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_FiftyNineFrames_DoesNotDealDamageYet()
    {
        var (system, _, _, health, _, eventBus) = Build();
        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        for (var frame = 0; frame < 59; frame++)
        {
            system.Update(default, 0);
        }

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void SteppingOffHazard_StopsFurtherDamage()
    {
        var (system, _, exposures, health, _, eventBus) = Build();
        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(5, 5, 0), new Vector3Int(6, 5, 0), new Vector2Byte(1, 1)));

        Assert.IsFalse(exposures.Has(MoverEntityId));

        for (var frame = 0; frame < 120; frame++)
        {
            system.Update(default, 0);
        }

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void HazardToHazardMove_RetriggersImmediateDamageAndResetsCountdown()
    {
        var (system, hazards, exposures, health, mapQuery, eventBus) = Build();
        const int secondTerrainEntityId = 101;
        hazards.Add(secondTerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: 60));
        mapQuery.SetTerrain(new Vector3Int(6, 5, 0), secondTerrainEntityId);

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(4, 5, 0), new Vector3Int(5, 5, 0), new Vector2Byte(1, 1)));
        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(default, 0);
        }

        eventBus.Publish(new EntityMoved(MoverEntityId, new Vector3Int(5, 5, 0), new Vector3Int(6, 5, 0), new Vector2Byte(1, 1)));

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(60, exposures.GetReadonly(MoverEntityId).FramesUntilNextTick);
    }
}
