using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.TerrainContacts.Components;
using Game.Modules.TerrainContacts.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.Tags;
using Game.Terrain;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.Modules.TerrainContacts;

[TestClass]
public sealed class TerrainContactSystemTests
{
    private const int MoverEntityId = 0;
    private const int TickIntervalFrames = 60;
    private static readonly int FirstTickDelay = (int)FrameDeadline.AfterStaggered(0, TickIntervalFrames, MoverEntityId);

    /// <summary>Minimal IMapQuery test double -- only GetTerrainAt is exercised by TerrainContactSystem, everything else is a fixed/empty answer.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int X, int Y, int Z), TerrainCell> _terrainByPosition = [];

        public MapBounds Bounds { get; } = new(0, 0, 20, 20, 3);
        public bool IsOnMap(Vector3Int position) => true;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public bool IsBlocking(int entityId) => true;

        public void SetTerrain(Vector3Int position, ushort terrainTypeId) => _terrainByPosition[(position.X, position.Y, position.Z)] = new TerrainCell(terrainTypeId, 0);

        public TerrainCell GetTerrainAt(Vector3Int position) =>
            _terrainByPosition.TryGetValue((position.X, position.Y, position.Z), out var cell) ? cell : default;

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);

        public Dictionary<(int X, int Y, int Z), List<int>> Occupants { get; } = [];

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            Occupants.TryGetValue((position.X, position.Y, position.Z), out var occupantIds) ? occupantIds : [];
    }

    /// <summary>
    /// Drives the system one real frame at a time: Update at the next frame number, then clear the
    /// move buffer -- the second half is normally SystemManager's job (see FrameEventBuffer's own
    /// doc comment), done here since these tests construct TerrainContactSystem directly. Frame
    /// numbers matter now: exposure ticks are absolute deadlines.
    /// </summary>
    private sealed class Harness(TerrainContactSystem system, FrameEventBuffer<EntityMovedEvent> movedEntities, DirectComponentPool<TransformComponent> transforms)
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

        public void Move(Vector3Int from, Vector3Int to)
        {
            transforms.Merge(MoverEntityId, new TransformComponent(to, new Vector2Byte(1, 1)));
            MovedEntities.Record(new EntityMovedEvent(MoverEntityId, from, to, new Vector2Byte(1, 1)));
        }
    }

    private static readonly Vector3Int OffHazard = new(4, 5, 0);
    private static readonly Vector3Int OnHazard = new(5, 5, 0);

    private static TerrainDefinition HazardTerrain(string key, BodyPartType? preferredTargetType = null) =>
        new(key, "Test hazard", "", default, "~", default, Contact: new TerrainContact([Damage(10)], RepeatEveryFrames: TickIntervalFrames, GroundContactBodyPart: preferredTargetType));

    private static Effect Damage(short amount) => new([new DirectDamage(amount, amount, BodyPart: BodyPartTargeting.GroundContact)]);

    private static DirectComponentPool<TransformComponent> CreateTransformPool() =>
        new(200, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<TerrainContactExposureComponent> CreateExposurePool() =>
        new(entityCapacity: 200, initialCapacity: 4, static (ref existing, incoming) => { });

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(entityCapacity: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<DeadComponent> CreateDeadPool() =>
        new(entityCapacity: 200, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    /// <summary>Complex-health counterpart to Build -- the mover carries body parts (Head/Torso, mirroring a Human-shaped fixture) instead of a SimpleHealthComponent, and the hazard's own PreferredTargetType is caller-supplied so a test can exercise either the type-match or the bottommost-fallback path.</summary>
    private static (Harness Harness, EntityBodyParts BodyParts) BuildComplex(BodyPartType? preferredTargetType)
    {
        var terrain = new TerrainRegistry();
        var partsWorld = new BodyPartTestWorld(
            new BodyPartTemplate("Head", BodyPartType.Head, 5, 100, IsVital: true),
            new BodyPartTemplate("Torso", BodyPartType.Torso, 4, 100, IsVital: true));
        partsWorld.Give(MoverEntityId);
        var bodyParts = partsWorld.BodyParts;
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        mapQuery.SetTerrain(OnHazard, terrain.Register(HazardTerrain("test:hazard", preferredTargetType)));

        var transforms = CreateTransformPool();
        var system = TestSystems.TerrainContactSystem(terrain, CreateExposurePool(), CreateHealthPool(), new EventBus(), mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), statModifiers: null, deadEntities: null, bodyParts: bodyParts, transforms: transforms);

        return (new Harness(system, movedEntities, transforms), bodyParts);
    }

    private static (
        Harness Harness,
        TerrainRegistry Terrain,
        PackedComponentPool<TerrainContactExposureComponent> Exposures,
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

        var transforms = CreateTransformPool();
        var system = TestSystems.TerrainContactSystem(terrain, exposures, health, new EventBus(), mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), statModifiers: null, deadEntities: deadEntities, transforms: transforms);

        return (new Harness(system, movedEntities, transforms), terrain, exposures, health, mapQuery, deadEntities);
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

        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Head), out var headPart);
        Assert.AreEqual(90, headPart.CurrentHealth);
        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Torso), out var torsoPart);
        Assert.AreEqual(100, torsoPart.CurrentHealth, "Torso must be untouched -- the hit landed on Head.");
    }

    [TestMethod]
    public void SteppingOntoHazard_PreferredTargetTypeAbsentOnMover_FallsBackToBottommost()
    {
        // The mover has no Foot part -- TerrainContactSystem hardcodes a Bottommost fallback for
        // every hazard with a PreferredTargetType set, so the hit must land on Torso (VerticalPosition
        // 4), the lower of the mover's two fixture parts, not Head (VerticalPosition 5).
        var (harness, bodyParts) = BuildComplex(preferredTargetType: BodyPartType.Foot);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Torso), out var torsoPart);
        Assert.AreEqual(90, torsoPart.CurrentHealth);
        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Head), out var headPart);
        Assert.AreEqual(100, headPart.CurrentHealth, "Head must be untouched -- the fallback landed on the bottommost part, Torso.");
    }

    /// <summary>A hazard that names no body part hits the bottommost one every time -- on stepping on and on every repeat while the mover stays -- never a part picked at random.</summary>
    [TestMethod]
    public void StandingOnHazardThatNamesNoBodyPart_EveryRepeatHitLandsOnTheBottommostPart()
    {
        var (harness, bodyParts) = BuildComplex(preferredTargetType: null);

        harness.Move(OffHazard, OnHazard);
        harness.Step();
        harness.StepThrough(harness.Frame + 5 * TickIntervalFrames);

        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Torso), out var torsoPart);
        Assert.AreEqual(40, torsoPart.CurrentHealth, "The hit on stepping on and all five repeats landed on Torso.");
        bodyParts.TryGet(MoverEntityId, BodyPartSelection.PickByType(bodyParts, MoverEntityId, BodyPartType.Head), out var headPart);
        Assert.AreEqual(100, headPart.CurrentHealth);
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
        exposures.Add(MoverEntityId, new TerrainContactExposureComponent(nextTickFrame: 60, hazardId));
        var clock = new SimulationClock();
        clock.Advance(630);
        var scope = new SimulationScope(static _ => false);
        _ = TestSystems.TerrainContactSystem(terrain, exposures, health, new EventBus(), new FakeMapQuery(), new TestPlayerQuery(-1), new FrameEventBuffer<EntityMovedEvent>(), new MathUtility(), clock, simulationScope: scope);

        scope.RaiseResumed(MoverEntityId);

        Assert.AreEqual(100f, health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(660u, exposures.GetReadonly(MoverEntityId).NextTickFrame);
    }

    /// <summary>Records each application it receives, for a test about when and in what order a contact's effects apply.</summary>
    private sealed class RecordingEntry(string name, List<string> applications) : IEffectEntry
    {
        public EffectOutcome Apply(in EffectContext context)
        {
            applications.Add($"{name} entity {context.TargetEntityId} terrain {context.Source.TerrainTypeId} frame {context.Now}");
            return EffectOutcome.Applied;
        }
    }

    private static Effect Recording(string name, List<string> applications) => new([new RecordingEntry(name, applications)]);

    private static readonly Vector3Int OnOtherTerrain = new(6, 5, 0);

    private static (Harness Harness, PackedComponentPool<TerrainContactExposureComponent> Exposures, ushort TerrainTypeId) BuildWith(TerrainContact contact)
    {
        var terrain = new TerrainRegistry();
        var exposures = CreateExposurePool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var terrainTypeId = terrain.Register(new TerrainDefinition("test:contact", "Test contact", "", default, ".", default, Contact: contact));
        mapQuery.SetTerrain(OnOtherTerrain, terrainTypeId);

        var transforms = CreateTransformPool();
        var system = TestSystems.TerrainContactSystem(terrain, exposures, CreateHealthPool(), new EventBus(), mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), transforms: transforms);

        return (new Harness(system, movedEntities, transforms), exposures, terrainTypeId);
    }

    [TestMethod]
    public void ContactWithSeveralEffects_AppliesEachInOrder()
    {
        var applications = new List<string>();
        var (harness, _, terrainTypeId) = BuildWith(new TerrainContact([Recording("first", applications), Recording("second", applications)]));

        harness.Move(OffHazard, OnOtherTerrain);
        harness.Step();

        CollectionAssert.AreEqual(
            new[] { $"first entity {MoverEntityId} terrain {terrainTypeId} frame {harness.Frame}", $"second entity {MoverEntityId} terrain {terrainTypeId} frame {harness.Frame}" },
            applications);
    }

    /// <summary>A contact with no repeat applies on stepping on and leaves nothing on the entity: standing there applies nothing more.</summary>
    [TestMethod]
    public void ContactWithoutRepeat_AppliesOnSteppingOnOnlyAndHoldsNoExposure()
    {
        var applications = new List<string>();
        var (harness, exposures, _) = BuildWith(new TerrainContact([Recording("effect", applications)]));

        harness.Move(OffHazard, OnOtherTerrain);
        harness.Step();
        harness.StepThrough(harness.Frame + 3 * TickIntervalFrames);

        Assert.HasCount(1, applications);
        Assert.IsFalse(exposures.Has(MoverEntityId));
    }

    [TestMethod]
    public void ContactWithRepeat_AppliesAgainEachInterval()
    {
        var applications = new List<string>();
        var (harness, exposures, _) = BuildWith(new TerrainContact([Recording("effect", applications)], RepeatEveryFrames: TickIntervalFrames));

        harness.Move(OffHazard, OnOtherTerrain);
        harness.Step();
        harness.StepThrough(harness.Frame + 2 * TickIntervalFrames);

        Assert.HasCount(3, applications);
        Assert.IsTrue(exposures.Has(MoverEntityId));
    }

    /// <summary>Stepping from a repeating contact onto one that doesn't repeat ends the repeat.</summary>
    [TestMethod]
    public void SteppingFromRepeatingContactOntoOneThatDoesNot_RemovesTheExposure()
    {
        var applications = new List<string>();
        var terrain = new TerrainRegistry();
        var exposures = CreateExposurePool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        mapQuery.SetTerrain(OnHazard, terrain.Register(new TerrainDefinition("test:repeating", "Repeating", "", default, ".", default,
            Contact: new TerrainContact([Recording("repeating", applications)], RepeatEveryFrames: TickIntervalFrames))));
        mapQuery.SetTerrain(OnOtherTerrain, terrain.Register(new TerrainDefinition("test:once", "Once", "", default, ".", default,
            Contact: new TerrainContact([Recording("once", applications)]))));
        var transforms = CreateTransformPool();
        var harness = new Harness(
            TestSystems.TerrainContactSystem(terrain, exposures, CreateHealthPool(), new EventBus(), mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), transforms: transforms),
            movedEntities, transforms);

        harness.Move(OffHazard, OnHazard);
        harness.Step();
        harness.Move(OnHazard, OnOtherTerrain);
        harness.Step();

        Assert.IsFalse(exposures.Has(MoverEntityId));
        Assert.HasCount(2, applications);
    }

    /// <summary>Contact damage carries its terrain's tags, so a resistance conditioned on one of them reduces it and an unrelated one doesn't.</summary>
    [TestMethod]
    [DataRow(true, 95)]
    [DataRow(false, 90)]
    public void ContactDamageTags_LetAConditionedResistanceReduceIt(bool damageIsFire, int expectedHealth)
    {
        var terrain = new TerrainRegistry();
        var health = CreateHealthPool();
        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 200, initialCapacity: 4);
        health.Add(MoverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        statModifiers.Add(MoverEntityId, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude: -0.5f, FrameDeadline.Never, ActionSource.Admin, GameTags.DamageFire));
        mapQuery.SetTerrain(OnHazard, terrain.Register(new TerrainDefinition("test:hazard", "Test hazard", "", default, ".", default,
            Contact: new TerrainContact([Damage(10)], Tags: damageIsFire ? [GameTags.DamageFire] : [GameTags.DamagePoison]))));
        var transforms = CreateTransformPool();
        var harness = new Harness(
            TestSystems.TerrainContactSystem(terrain, CreateExposurePool(), health, new EventBus(), mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(), statModifiers: statModifiers, transforms: transforms),
            movedEntities, transforms);

        harness.Move(OffHazard, OnHazard);
        harness.Step();

        Assert.AreEqual(expectedHealth, health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>Everything a test about terrain changing, or about what a contact's effects did, needs to reach.</summary>
    private sealed record ChangeFixture(
        Harness Harness,
        TerrainRegistry Terrain,
        FakeMapQuery MapQuery,
        EventBus EventBus,
        ComponentManager Components,
        PackedComponentPool<TerrainContactExposureComponent> Exposures,
        PackedComponentPool<SimpleHealthComponent> Health,
        TestFloatingText FloatingText)
    {
        public ushort Plain { get; } = Terrain.Register(new TerrainDefinition("test:plain", "Plain", "", default, ".", default));

        /// <summary>Puts the mover on position without a move, the way an entity is simply standing somewhere.</summary>
        public void Stand(Vector3Int position)
        {
            Components.Merge(MoverEntityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            MapQuery.Occupants[(position.X, position.Y, position.Z)] = [MoverEntityId];
        }

        /// <summary>Changes a cell's terrain and announces it, as World.SetTerrain does.</summary>
        public void ChangeTerrain(Vector3Int position, ushort terrainTypeId)
        {
            var previousTypeId = MapQuery.GetTerrainAt(position).TypeId;
            MapQuery.SetTerrain(position, terrainTypeId);
            EventBus.Publish(new TerrainChangedEvent(position.X, position.Y, (TerrainLayer)position.Z, previousTypeId, terrainTypeId));
        }
    }

    private static ChangeFixture BuildForChanges(StatusEffectApplierRegistry? statusEffectAppliers = null)
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        var terrain = new TerrainRegistry();
        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var exposures = components.GetPackedPool<TerrainContactExposureComponent>();
        var health = components.GetPackedPool<SimpleHealthComponent>();
        var transforms = components.GetDirectPool<TransformComponent>();
        var floatingText = new TestFloatingText().Place(MoverEntityId, ProcessingTierLevel.Local);
        health.Add(MoverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));

        var system = TestSystems.TerrainContactSystem(terrain, exposures, health, eventBus, mapQuery, new TestPlayerQuery(MoverEntityId), movedEntities, new MathUtility(), new SimulationClock(),
            statModifiers: components.GetMultiPool<StatModifierComponent>(), floatingTextFeed: floatingText.Feed, transforms: transforms, statusEffectAppliers: statusEffectAppliers, componentManager: components);

        return new ChangeFixture(new Harness(system, movedEntities, transforms), terrain, mapQuery, eventBus, components, exposures, health, floatingText);
    }

    [TestMethod]
    public void TerrainChangedToAHazardUnderAStandingEntity_AppliesItAtOnceAndStartsTheRepeat()
    {
        var fixture = BuildForChanges();
        var hazard = fixture.Terrain.Register(HazardTerrain("test:hazard"));
        fixture.Stand(OnHazard);
        fixture.Harness.Step();

        fixture.ChangeTerrain(OnHazard, hazard);

        Assert.AreEqual(90, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.IsTrue(fixture.Exposures.Has(MoverEntityId));

        fixture.Harness.StepThrough(fixture.Harness.Frame + 2 * TickIntervalFrames);
        Assert.AreEqual(70, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void TerrainChangedAwayFromAHazardUnderAStandingEntity_EndsTheExposureAtOnce()
    {
        var fixture = BuildForChanges();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(HazardTerrain("test:hazard")));
        fixture.Stand(OffHazard);
        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.MapQuery.Occupants[(OnHazard.X, OnHazard.Y, OnHazard.Z)] = [MoverEntityId];
        fixture.Harness.Step();

        fixture.ChangeTerrain(OnHazard, fixture.Plain);

        Assert.IsFalse(fixture.Exposures.Has(MoverEntityId));
        fixture.Harness.StepThrough(fixture.Harness.Frame + 3 * TickIntervalFrames);
        Assert.AreEqual(90, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth, "Only the hit on stepping on.");
    }

    /// <summary>A repeat reads the terrain where the entity stands, so terrain that changed with nothing announcing it still ends the exposure at the next repeat.</summary>
    [TestMethod]
    public void TerrainReplacedWithoutAnyEvent_TheNextRepeatFindsItGoneAndEndsTheExposure()
    {
        var fixture = BuildForChanges();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(HazardTerrain("test:hazard")));
        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        fixture.MapQuery.SetTerrain(OnHazard, fixture.Plain);
        fixture.Harness.StepThrough(fixture.Harness.Frame + 3 * TickIntervalFrames);

        Assert.IsFalse(fixture.Exposures.Has(MoverEntityId));
        Assert.AreEqual(90, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>A repeat applies the contact of the terrain under the entity now, even when that is a different hazard than the one it stepped onto.</summary>
    [TestMethod]
    public void TerrainReplacedByAnotherHazardWithoutAnyEvent_TheNextRepeatAppliesTheNewOne()
    {
        var fixture = BuildForChanges();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(HazardTerrain("test:hazard")));
        var strongerHazard = fixture.Terrain.Register(new TerrainDefinition("test:stronger", "Stronger", "", default, "~", default,
            Contact: new TerrainContact([Damage(25)], RepeatEveryFrames: TickIntervalFrames)));
        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        fixture.MapQuery.SetTerrain(OnHazard, strongerHazard);
        fixture.Harness.StepThrough(fixture.Harness.Frame + FirstTickDelay);

        Assert.AreEqual(65, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(strongerHazard, fixture.Exposures.GetReadonly(MoverEntityId).TerrainTypeId);
    }

    /// <summary>A contact's effects are read from the definition at every application, so replacing the definition changes what the next repeat does.</summary>
    [TestMethod]
    public void DefinitionReplacedWithDifferentEffects_TheNextRepeatAppliesTheNewOnes()
    {
        var fixture = BuildForChanges();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(HazardTerrain("test:hazard")));
        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        fixture.Terrain.Register(new TerrainDefinition("test:hazard", "Test hazard", "", default, "~", default,
            Contact: new TerrainContact([Damage(25)], RepeatEveryFrames: TickIntervalFrames)));
        fixture.Harness.StepThrough(fixture.Harness.Frame + FirstTickDelay);

        Assert.AreEqual(65, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DefinitionGainsAContact_WhoeverStandsOnThatTerrainStepsOntoIt()
    {
        var fixture = BuildForChanges();
        var terrainTypeId = fixture.Terrain.Register(new TerrainDefinition("test:hazard", "Test hazard", "", default, "~", default));
        fixture.MapQuery.SetTerrain(OnHazard, terrainTypeId);
        fixture.Stand(OnHazard);
        fixture.Harness.Step();

        fixture.Terrain.Register(HazardTerrain("test:hazard"));

        Assert.AreEqual(90, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
        Assert.AreEqual(terrainTypeId, fixture.Exposures.GetReadonly(MoverEntityId).TerrainTypeId);
    }

    [TestMethod]
    public void DefinitionLosesItsContact_TheExposureEndsOnItsNextRepeat()
    {
        var fixture = BuildForChanges();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(HazardTerrain("test:hazard")));
        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        fixture.Terrain.Register(new TerrainDefinition("test:hazard", "Test hazard", "", default, "~", default));
        fixture.Harness.StepThrough(fixture.Harness.Frame + 2 * TickIntervalFrames);

        Assert.IsFalse(fixture.Exposures.Has(MoverEntityId));
        Assert.AreEqual(90, fixture.Health.GetReadonly(MoverEntityId).CurrentHealth);
    }

    /// <summary>A contact can hold any effect an action can: here a status effect, which an immune entity refuses -- reported once for the stay, not on every repeat.</summary>
    [TestMethod]
    public void ContactGrantingAStatusEffectToAnImmuneEntity_ReportsTheRefusalOncePerStay()
    {
        var appliers = new StatusEffectApplierRegistry();
        var fixture = BuildForChanges(appliers);
        var components = fixture.Components;
        appliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, components.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks,
            (entityId, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(components, new EntityKeys(), entityId, count, source, durationInTicks: 5, now, fixture.EventBus, new TestPlayerQuery(MoverEntityId), announcesRefusal)));
        StatusEffectImmunityEffects.GrantPermanent(components.GetMultiPool<StatusEffectImmunityComponent>(), MoverEntityId, StatusEffectType.Poison);
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(new TerrainDefinition("test:swamp", "Swamp", "", default, "~", default,
            Contact: new TerrainContact([new Effect([new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3)])], RepeatEveryFrames: TickIntervalFrames))));
        var blockedEvents = 0;
        fixture.EventBus.Subscribe<StatusEffectImmunityBlockedEvent>(_ => blockedEvents++);

        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();
        fixture.Harness.StepThrough(fixture.Harness.Frame + 4 * TickIntervalFrames);

        Assert.IsTrue(fixture.Exposures.GetReadonly(MoverEntityId).Refused);
        Assert.AreEqual(1, blockedEvents);
        Assert.AreEqual(1, fixture.FloatingText.Published.Count(text => text.Kind == FloatingTextKind.Immune));
    }

    [TestMethod]
    public void ContactGrantingAStatusEffect_HoldsItsStacksOnWhoeverStandsThere()
    {
        var appliers = new StatusEffectApplierRegistry();
        var fixture = BuildForChanges(appliers);
        var components = fixture.Components;
        appliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, components.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks,
            (entityId, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(components, new EntityKeys(), entityId, count, source, durationInTicks: 5, now, fixture.EventBus, TestPlayerQuery.NoPlayer, announcesRefusal)));
        var swamp = fixture.Terrain.Register(new TerrainDefinition("test:swamp", "Swamp", "", default, "~", default,
            Contact: new TerrainContact([new Effect([new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3, StatusEffectGrantMode.TopUpTo)])], RepeatEveryFrames: TickIntervalFrames)));
        fixture.MapQuery.SetTerrain(OnHazard, swamp);

        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        var poison = components.GetPackedPool<PoisonTimerComponent>().GetReadonly(MoverEntityId);
        Assert.AreEqual(3, poison.StackCount);
        Assert.AreEqual(ActionSource.FromTerrain(swamp), poison.Source);
        Assert.IsFalse(fixture.Exposures.GetReadonly(MoverEntityId).Refused);
    }

    /// <summary>Holy Ground's shape: a repeating contact granting a modifier that refreshes, held once however long the entity stands there and lasting its own duration after the latest application.</summary>
    [TestMethod]
    public void RepeatingContactWithARefreshingModifier_HoldsOneModifierAndKeepsMovingItsExpiry()
    {
        const ushort BlessingFrames = 600;
        var fixture = BuildForChanges();
        var statModifiers = fixture.Components.GetMultiPool<StatModifierComponent>();
        fixture.MapQuery.SetTerrain(OnHazard, fixture.Terrain.Register(new TerrainDefinition("test:blessed", "Blessed", "", default, "*", default,
            Contact: new TerrainContact(
                [new Effect([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, BlessingFrames, Stacking: StatModifierStacking.RefreshFromSameSource)])],
                RepeatEveryFrames: TickIntervalFrames))));

        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();
        var steppedOn = fixture.Harness.Frame;
        fixture.Harness.StepThrough(steppedOn + FirstTickDelay + 2 * TickIntervalFrames);

        Assert.AreEqual(1, statModifiers.CountForEntity(MoverEntityId));
        var blessing = statModifiers.GetReadonlyByDenseIndex(statModifiers.GetFirstDenseIndex(MoverEntityId));
        Assert.AreEqual((uint)(steppedOn + FirstTickDelay + 2 * TickIntervalFrames + BlessingFrames), blessing.ExpiresAtFrame);
    }

    [TestMethod]
    public void ContactDamage_IsNamedAfterTheTerrainAndAttributedToIt()
    {
        var fixture = BuildForChanges();
        var hazard = fixture.Terrain.Register(HazardTerrain("test:hazard"));
        fixture.MapQuery.SetTerrain(OnHazard, hazard);
        EntityDamagedEvent? damaged = null;
        fixture.EventBus.Subscribe<EntityDamagedEvent>(e => damaged = e);

        fixture.Harness.Move(OffHazard, OnHazard);
        fixture.Harness.Step();

        Assert.IsNotNull(damaged);
        Assert.AreEqual("Test hazard", damaged.Value.DamageType);
        Assert.AreEqual(ActionSource.FromTerrain(hazard), damaged.Value.Source);
    }
}
