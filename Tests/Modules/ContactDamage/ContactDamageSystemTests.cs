using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Terrain;
using Game.World;

namespace Tests.Modules.ContactDamage;

[TestClass]
public sealed class ContactDamageSystemTests
{
    private const int MoverEntityId = 0;
    private const int TickIntervalFrames = 60;
    private static readonly int FirstTickDelay = (int)FrameDeadline.AfterStaggered(0, TickIntervalFrames, MoverEntityId);

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
        public Engine.ECS.Entities.EntityKey PlayerEntityKey { get; init; } = TestSources.KeyOf(playerEntityId);
    }

    /// <summary>Minimal IMapQuery test double -- only GetTerrainAt is exercised by ContactDamageSystem, everything else is a fixed/empty answer.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int X, int Y, int Z), TerrainCell> _terrainByPosition = [];

        public MapBounds Bounds { get; } = new(0, 0, 1000, 1000, 3);
        public bool IsOnMap(Vector3Int position) => true;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public bool IsBlocking(int entityId) => true;

        public void SetTerrain(Vector3Int position, ushort terrainTypeId) => _terrainByPosition[(position.X, position.Y, position.Z)] = new TerrainCell(terrainTypeId, 0);

        public TerrainCell GetTerrainAt(Vector3Int position) =>
            _terrainByPosition.TryGetValue((position.X, position.Y, position.Z), out var cell) ? cell : default;

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);
    }

    /// <summary>
    /// Drives the system one real frame at a time: Update at the next frame number, then clear the
    /// move buffer -- the second half is normally SystemManager's job (see FrameEventBuffer's own
    /// doc comment), done here since these tests construct ContactDamageSystem directly. Frame
    /// numbers matter now: exposure ticks are absolute deadlines.
    /// </summary>
    private sealed class Harness(ContactDamageSystem system, FrameEventBuffer<EntityMovedEvent> movedEntities)
    {
        public long Frame { get; private set; } = -1;

        public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; } = movedEntities;

        public void Step()
        {
            Frame++;
            system.Update(new EngineTime(default, default, false, Frame), 0);
            MovedEntities.ClearFrame();
        }

        public void StepThrough(long lastFrame)
        {
            while (Frame < lastFrame)
            {
                Step();
            }
        }

        public void Move(Vector3Int from, Vector3Int to) =>
            MovedEntities.Record(new EntityMovedEvent(MoverEntityId, from, to, new Vector2Byte(1, 1)));
    }

    private static readonly Vector3Int OffHazard = new(4, 5, 0);
    private static readonly Vector3Int OnHazard = new(5, 5, 0);

    private static TerrainDefinition HazardTerrain(string key, BodyPartType? preferredTargetType = null) =>
        new(key, "Test hazard", "", default, "~", default, ContactHazard: new ContactHazard(DamagePerTick: 10, TickIntervalFrames: TickIntervalFrames, preferredTargetType));

    private static PackedComponentPool<ContactDamageExposureComponent> CreateExposurePool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Complex-health counterpart to Build -- the mover carries BodyPartComponents (Head/Torso, mirroring a Human-shaped fixture) instead of a SimpleHealthComponent, and the hazard's own PreferredTargetType is caller-supplied so a test can exercise either the type-match or the bottommost-fallback path.</summary>
    private static (Harness Harness, MultiComponentPool<BodyPartComponent> BodyParts) BuildComplex(BodyPartType? preferredTargetType)
    {
        var terrain = new TerrainRegistry();
        var bodyParts = new MultiComponentPool<BodyPartComponent>(maximumEntityCount: 200, initialCapacity: 8);
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        bodyParts.Add(MoverEntityId, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 5, currentHealth: 100, maximumHealth: 100, isVital: true));
        bodyParts.Add(MoverEntityId, new BodyPartComponent("Torso", BodyPartType.Torso, 0, verticalPosition: 4, currentHealth: 100, maximumHealth: 100, isVital: true));
        mapQuery.SetTerrain(OnHazard, terrain.Register(HazardTerrain("test:hazard", preferredTargetType)));

        var system = new ContactDamageSystem(terrain, CreateExposurePool(), CreateHealthPool(), new EventBus(), mapQuery, new FakePlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), statModifiers: null, deadEntities: null, bodyParts: bodyParts);

        return (new Harness(system, movedEntities), bodyParts);
    }

    private static (
        Harness Harness,
        TerrainRegistry Terrain,
        PackedComponentPool<ContactDamageExposureComponent> Exposures,
        PackedComponentPool<SimpleHealthComponent> Health,
        FakeMapQuery MapQuery,
        PackedComponentPool<DeadComponent> DeadEntities) Build()
    {
        var terrain = new TerrainRegistry();
        var exposures = CreateExposurePool();
        var health = CreateHealthPool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var deadEntities = CreateDeadPool();

        health.Add(MoverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        mapQuery.SetTerrain(OnHazard, terrain.Register(HazardTerrain("test:hazard")));

        var system = new ContactDamageSystem(terrain, exposures, health, new EventBus(), mapQuery, new FakePlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), statModifiers: null, deadEntities: deadEntities);

        return (new Harness(system, movedEntities), terrain, exposures, health, mapQuery, deadEntities);
    }

    [TestMethod]
    public void SteppingOntoHazard_DealsImmediateDamage()
    {
        var (harness, _, _, health, _, _) = Build();

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>The next tick is the mover's staggered first deadline after the frame it stepped on -- an absolute deadline, not a countdown that also ticks once on the entry frame.</summary>
    [TestMethod]
    public void SteppingOntoHazard_SchedulesNextTickOnItsStaggeredDeadline()
    {
        var (harness, _, exposures, _, _, _) = Build();
        harness.StepThrough(10);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        Assert.IsTrue(exposures.Has(MoverEntityId));
        Assert.AreEqual((uint)(harness.Frame + FirstTickDelay), exposures.GetReadonly(MoverEntityId).NextTickFrame);
    }

    [TestMethod]
    public void SteppingOntoNonHazardTile_GrantsNoExposure()
    {
        var (harness, _, exposures, health, _, _) = Build();

        harness.Move(OffHazard, new Vector3Int(4, 6, 0));
        harness.Step();

        Assert.IsFalse(exposures.Has(MoverEntityId));
        Assert.AreEqual(100, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_DealsDamageAgainExactlyOnItsFirstDeadline()
    {
        var (harness, _, _, health, _, _) = Build();
        harness.Move(OffHazard, OnHazard);
        harness.Step();
        var steppedOn = harness.Frame;

        harness.StepThrough(steppedOn + FirstTickDelay - 1);
        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth, "Not yet -- one frame short of the deadline.");

        harness.Step();
        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void RemainingOnHazard_KeepsTickingEveryInterval()
    {
        var (harness, _, _, health, _, _) = Build();
        harness.Move(OffHazard, OnHazard);
        harness.Step();

        harness.StepThrough(harness.Frame + 3 * TickIntervalFrames);

        Assert.AreEqual(60, health.GetReadonly(MoverEntityId).CurrentHealth, "Entry hit plus three interval ticks.");
    }

    [TestMethod]
    public void DeadEntityAlreadyExposed_DoesNotTakeFurtherDamage()
    {
        var (harness, _, _, health, _, deadEntities) = Build();
        harness.Move(OffHazard, OnHazard);
        harness.Step(); // Onto the hazard: exposure added, immediate 10 damage -> 90.
        deadEntities.Add(MoverEntityId, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        harness.StepThrough(harness.Frame + 3 * TickIntervalFrames);

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth, "A corpse standing in lava must not keep taking contact damage forever.");
    }

    [TestMethod]
    public void SteppingOffHazard_StopsFurtherDamage()
    {
        var (harness, _, exposures, health, _, _) = Build();
        harness.Move(OffHazard, OnHazard);
        harness.Move(OnHazard, new Vector3Int(6, 5, 0));
        harness.Step(); // Drains both buffered moves: onto the hazard (adds exposure + damage), then off it (removes the exposure).

        Assert.IsFalse(exposures.Has(MoverEntityId));

        harness.StepThrough(harness.Frame + 2 * TickIntervalFrames);

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void HazardToHazardMove_RetriggersImmediateDamageAndReschedulesFromTheMove()
    {
        var (harness, terrain, exposures, health, mapQuery, _) = Build();
        var secondHazard = new Vector3Int(6, 5, 0);
        mapQuery.SetTerrain(secondHazard, terrain.Register(HazardTerrain("test:second-hazard")));

        harness.Move(OffHazard, OnHazard);
        harness.Step();
        harness.StepThrough(harness.Frame + FirstTickDelay / 3);

        harness.Move(OnHazard, secondHazard);
        harness.Step();
        var movedOn = harness.Frame;

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual((uint)(movedOn + FirstTickDelay), exposures.GetReadonly(MoverEntityId).NextTickFrame);

        harness.StepThrough(movedOn + FirstTickDelay - 1);
        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth, "The first hazard's old tick frame passed mid-way -- it must not also fire.");
    }

    [TestMethod]
    public void SteppingOntoHazard_PreferredTargetTypePresentOnMover_DealsDamageToThatType()
    {
        var (harness, bodyParts) = BuildComplex(preferredTargetType: BodyPartType.Head);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        var headDenseIndex = BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Head);
        Assert.AreEqual(90, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth);
        var torsoDenseIndex = BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Torso);
        Assert.AreEqual(100, bodyParts.GetReadonlyByDenseIndex(torsoDenseIndex).CurrentHealth, "Torso must be untouched -- the hit landed on Head.");
    }

    [TestMethod]
    public void SteppingOntoHazard_PreferredTargetTypeAbsentOnMover_FallsBackToBottommost()
    {
        // The mover has no Foot part -- ContactDamageSystem hardcodes a Bottommost fallback for
        // every hazard with a PreferredTargetType set, so the hit must land on Torso (VerticalPosition
        // 4), the lower of the mover's two fixture parts, not Head (VerticalPosition 5).
        var (harness, bodyParts) = BuildComplex(preferredTargetType: BodyPartType.Foot);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        var torsoDenseIndex = BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Torso);
        Assert.AreEqual(90, bodyParts.GetReadonlyByDenseIndex(torsoDenseIndex).CurrentHealth);
        var headDenseIndex = BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Head);
        Assert.AreEqual(100, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth, "Head must be untouched -- the fallback landed on the bottommost part, Torso.");
    }

    /// <summary>An exposure doesn't accrue while its entity is frozen: a creature frozen in lava for ten seconds takes none of those ten ticks when it resumes, and its next tick keeps the cadence it had.</summary>
    [TestMethod]
    public void EntityResumed_OwedExposureTicks_AreSkippedNotDealt()
    {
        var terrain = new TerrainRegistry();
        var hazardId = terrain.Register(HazardTerrain("test:hazard"));
        var exposures = CreateExposurePool();
        var health = CreateHealthPool();
        health.Add(MoverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        exposures.Add(MoverEntityId, new ContactDamageExposureComponent(nextTickFrame: 60, hazardId));
        var clock = new SimulationClock();
        clock.Advance(630);
        var scope = new SimulationScope();
        scope.SetPolicy(static _ => false);
        _ = new ContactDamageSystem(terrain, exposures, health, new EventBus(), new FakeMapQuery(), new FakePlayerQuery(-1), new FrameEventBuffer<EntityMovedEvent>(), new MathUtility(), clock, simulationScope: scope);

        scope.RaiseResumed(MoverEntityId);

        Assert.AreEqual(100f, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(660u, exposures.GetReadonly(MoverEntityId).NextTickFrame);
    }
}
