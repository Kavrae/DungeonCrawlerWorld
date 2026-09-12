using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.ContactDamage.Components;
using Game.Modules.ContactDamage.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.World;

namespace Tests.Modules.ContactDamage;

[TestClass]
public sealed class ContactDamageSystemTests
{
    private const int TerrainEntityId = 100;
    private const int MoverEntityId = 0;
    private const int TickIntervalFrames = 60;

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

    private static PackedComponentPool<DamageOnContactComponent> CreateHazardPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<ContactDamageExposureComponent> CreateExposurePool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(maximumEntityCount: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Complex-health counterpart to Build -- the mover carries BodyPartComponents (Head/Torso, mirroring a Human-shaped fixture) instead of a SimpleHealthComponent, and the hazard's own PreferredTargetType is caller-supplied so a test can exercise either the type-match or the bottommost-fallback path.</summary>
    private static (Harness Harness, MultiComponentPool<BodyPartComponent> BodyParts) BuildComplex(BodyPartType? preferredTargetType)
    {
        var hazards = CreateHazardPool();
        var bodyParts = new MultiComponentPool<BodyPartComponent>(maximumEntityCount: 200, initialCapacity: 8);
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        bodyParts.Add(MoverEntityId, new BodyPartComponent("Head", BodyPartType.Head, 0, verticalPosition: 5, currentHealth: 100, maximumHealth: 100, isVital: true));
        bodyParts.Add(MoverEntityId, new BodyPartComponent("Torso", BodyPartType.Torso, 0, verticalPosition: 4, currentHealth: 100, maximumHealth: 100, isVital: true));
        hazards.Add(TerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: TickIntervalFrames, preferredTargetType: preferredTargetType));
        mapQuery.SetTerrain(OnHazard, TerrainEntityId);

        var system = new ContactDamageSystem(hazards, CreateExposurePool(), CreateHealthPool(), new EventBus(), mapQuery, new FakePlayerQuery(MoverEntityId), movedEntities, new MathUtility(), statModifiers: null, deadEntities: null, bodyParts: bodyParts);

        return (new Harness(system, movedEntities), bodyParts);
    }

    private static (
        Harness Harness,
        PackedComponentPool<DamageOnContactComponent> Hazards,
        PackedComponentPool<ContactDamageExposureComponent> Exposures,
        PackedComponentPool<SimpleHealthComponent> Health,
        FakeMapQuery MapQuery,
        PackedComponentPool<DeadComponent> DeadEntities) Build()
    {
        var hazards = CreateHazardPool();
        var exposures = CreateExposurePool();
        var health = CreateHealthPool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var deadEntities = CreateDeadPool();

        health.Add(MoverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        hazards.Add(TerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: TickIntervalFrames));
        mapQuery.SetTerrain(OnHazard, TerrainEntityId);

        var system = new ContactDamageSystem(hazards, exposures, health, new EventBus(), mapQuery, new FakePlayerQuery(MoverEntityId), movedEntities, new MathUtility(), statModifiers: null, deadEntities: deadEntities);

        return (new Harness(system, movedEntities), hazards, exposures, health, mapQuery, deadEntities);
    }

    [TestMethod]
    public void SteppingOntoHazard_DealsImmediateDamage()
    {
        var (harness, _, _, health, _, _) = Build();

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>The next tick is TickIntervalFrames after the frame the entity stepped on -- an absolute deadline, not a countdown that also ticks once on the entry frame.</summary>
    [TestMethod]
    public void SteppingOntoHazard_SchedulesNextTickOneIntervalLater()
    {
        var (harness, _, exposures, _, _, _) = Build();
        harness.StepThrough(10);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        Assert.IsTrue(exposures.Has(MoverEntityId));
        Assert.AreEqual((uint)(harness.Frame + TickIntervalFrames), exposures.GetReadonly(MoverEntityId).NextTickFrame);
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
    public void RemainingOnHazard_DealsDamageAgainExactlyOneIntervalLater()
    {
        var (harness, _, _, health, _, _) = Build();
        harness.Move(OffHazard, OnHazard);
        harness.Step();
        var steppedOn = harness.Frame;

        harness.StepThrough(steppedOn + TickIntervalFrames - 1);
        Assert.AreEqual(90, health.GetReadonly(MoverEntityId).CurrentHealth, "Not yet -- one frame short of the interval.");

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
        deadEntities.Add(MoverEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

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
        var (harness, hazards, exposures, health, mapQuery, _) = Build();
        const int secondTerrainEntityId = 101;
        var secondHazard = new Vector3Int(6, 5, 0);
        hazards.Add(secondTerrainEntityId, new DamageOnContactComponent(damagePerTick: 10, tickIntervalFrames: TickIntervalFrames));
        mapQuery.SetTerrain(secondHazard, secondTerrainEntityId);

        harness.Move(OffHazard, OnHazard);
        harness.Step();
        harness.StepThrough(harness.Frame + 30);

        harness.Move(OnHazard, secondHazard);
        harness.Step();
        var movedOn = harness.Frame;

        Assert.AreEqual(80, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual((uint)(movedOn + TickIntervalFrames), exposures.GetReadonly(MoverEntityId).NextTickFrame);

        harness.StepThrough(movedOn + TickIntervalFrames - 1);
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
}
