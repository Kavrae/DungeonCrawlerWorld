using Engine.ECS.Entities;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Core.Components;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Death.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Auras.Systems;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraSystemTests
{
    private const int SourceEntityId = 100;
    private const int ObserverEntityId = 0;

    private static readonly Vector3Int SourcePosition = new(10, 10, 0);
    private static readonly Vector2Byte UnitSize = new(1, 1);

    /// <summary>
    /// Minimal IMapQuery test double with a real per-cell occupant dictionary -- backs
    /// AuraSystem's rare "a source moved" re-check path (StartExposuresNear/
    /// ReEvaluateExposuresNear), now a GetOccupantEntityIdsAt-per-cell walk rather than a
    /// GetEntityIdsInBox scan, now that per-mover detection is an O(1) AuraGrid lookup, not a
    /// live scan. Keeps the Blocking slot (SetOccupant/GetEntityIdAt/GetEntityIdsInBox) and the
    /// non-Blocking index (SetNonBlockingOccupant) as genuinely separate stores, mirroring
    /// Map's own split, so a test can prove GetOccupantEntityIdsAt -- and only that -- sees a
    /// non-Blocking occupant.
    /// </summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int X, int Y, int Z), int> _occupantByPosition = [];
        private readonly Dictionary<(int X, int Y, int Z), List<int>> _nonBlockingOccupantsByPosition = [];
        private readonly Dictionary<(int X, int Y, int Z), TerrainCell> _terrainByPosition = [];

        public MapBounds Bounds { get; init; } = new(0, 0, 1000, 1000, 3);
        /// <summary>One position that reads as off the map, standing in for where an entity sits before it is placed.</summary>
        public Vector3Int? OffMapPosition { get; init; }

        public bool IsOnMap(Vector3Int position) => position != OffMapPosition;
        public bool IsBlocking(int entityId) => true;

        public void SetOccupant(Vector3Int position, int entityId) => _occupantByPosition[(position.X, position.Y, position.Z)] = entityId;
        public void ClearOccupant(Vector3Int position) => _occupantByPosition.Remove((position.X, position.Y, position.Z));

        public void SetNonBlockingOccupant(Vector3Int position, int entityId)
        {
            var key = (position.X, position.Y, position.Z);
            if (!_nonBlockingOccupantsByPosition.TryGetValue(key, out var entityIds))
            {
                entityIds = [];
                _nonBlockingOccupantsByPosition[key] = entityIds;
            }
            entityIds.Add(entityId);
        }

        public int GetEntityIdAt(Vector3Int position) => _occupantByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;
        public void SetTerrain(Vector3Int position, ushort terrainTypeId) => _terrainByPosition[(position.X, position.Y, position.Z)] = new TerrainCell(terrainTypeId, 0);

        public TerrainCell GetTerrainAt(Vector3Int position) => _terrainByPosition.TryGetValue((position.X, position.Y, position.Z), out var cell) ? cell : default;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position)
        {
            var result = new List<int>();
            if (GetEntityIdAt(position) is var blockingId && blockingId != -1)
            {
                result.Add(blockingId);
            }

            if (_nonBlockingOccupantsByPosition.TryGetValue((position.X, position.Y, position.Z), out var nonBlockingIds))
            {
                result.AddRange(nonBlockingIds);
            }

            return result;
        }

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => Fill(box, entityIds, GetEntityIdAt);

        private static void Fill(CubeInt box, Span<int> entityIds, Func<Vector3Int, int> lookup)
        {
            var index = 0;
            for (var y = box.Position.Y; y < box.Position.Y + box.Size.Y; y++)
            {
                for (var x = box.Position.X; x < box.Position.X + box.Size.X; x++)
                {
                    entityIds[index] = lookup(new Vector3Int(x, y, box.Position.Z));
                    index++;
                }
            }
        }
    }

    /// <summary>
    /// The one simulation clock every Update and every AuraSource event handler in a test reads
    /// -- the way SystemManager.Clock is in the real game. Exposures live on a timer wheel, which
    /// needs time to only move forward, so every call goes through Step: one frame after the
    /// last, never a frame number chosen per call.
    /// </summary>
    private readonly SimulationClock _clock = new();

    /// <summary>The field the last Build made, for a test that reads the reach or the glow directly.</summary>
    private AuraField _auraField = null!;
    private AuraCatalog _auras = null!;

    /// <summary>More frames than a queued non-Local source waits for its resync when it is the only one queued.</summary>
    private static int GenerousCatchUpFrameCount(AuraSystem system) => 16;

    /// <summary>Advances the clock one frame and runs the system on it.</summary>
    private void Step(AuraSystem system)
    {
        _clock.Advance(_clock.CurrentFrame + 1);
        system.Update(new EngineTime(default, default, false, FrameCount: _clock.CurrentFrame), 0);
    }

    /// <summary>Runs frameCount consecutive frames, clearing the move buffer after each the way SystemManager does at the end of a real frame.</summary>
    private void RunFrames(AuraSystem system, FrameEventBuffer<EntityMovedEvent> movedEntities, int frameCount)
    {
        for (var i = 0; i < frameCount; i++)
        {
            Step(system);
            movedEntities.ClearFrame();
        }
    }

    /// <summary>Mirrors real game wiring (both BurningModule.Configure and PoisonModule.Configure registering their own applier into the same shared registry) -- the registry a caller can override via applierRegistry to exercise unsupported-effect-type behavior instead.</summary>
    private (AuraSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, FrameEventBuffer<EntityMovedEvent> MovedEntities, EventBus EventBus) Build(StatusEffectApplierRegistry? applierRegistry = null, TerrainRegistry? terrain = null, FakeMapQuery? mapQuery = null, SimulationScope? simulationScope = null, FloatingTextFeed? floatingTextFeed = null)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 200, initialComponentCapacity: 50));

        mapQuery ??= new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var eventBus = new EventBus();
        var auras = TestAuras.Catalog();
        _auras = auras;
        _auraField = new AuraField(mapQuery, terrain ?? new TerrainRegistry(), auras, eventBus);

        var system = new AuraSystem(
            componentManager.GetMultiPool<AuraExposureComponent>(),
            componentManager.GetMultiPool<AuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            mapQuery,
            eventBus,
            auras,
            movedEntities,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _clock,
            _auraField,
            TestActionEffects.Services(componentManager, new EntityKeys(), eventBus, new MathUtility(),
                componentManager.GetPackedPool<SimpleHealthComponent>(),
                statusEffectAppliers: applierRegistry ?? DefaultApplierRegistry(componentManager),
                statModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
                deadEntities: componentManager.GetPackedPool<DeadComponent>(),
                floatingTextFeed: floatingTextFeed),
            simulationScope: simulationScope ?? new SimulationScope(static _ => true));

        return (system, componentManager, mapQuery, movedEntities, eventBus);
    }

    private static StatusEffectApplierRegistry DefaultApplierRegistry(ComponentManager componentManager)
    {
        var registry = new StatusEffectApplierRegistry();
        registry.Register(new TimerBasedStatusEffectApplier<BurningTimerComponent>(StatusEffectType.Burning, componentManager.GetPackedPool<BurningTimerComponent>(), BurningEffects.MaxStacks, (id, count, source, now, announcesRefusal) => BurningEffects.ApplyStacks(componentManager, id, count, source, now, new EventBus(), TestPlayerQuery.NoPlayer, announcesRefusal)));
        registry.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(StatusEffectType.Poison, componentManager.GetPackedPool<PoisonTimerComponent>(), PoisonEffects.MaxStacks, (id, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(componentManager, new EntityKeys(), id, count, source, durationInTicks: 1, now, new EventBus(), TestPlayerQuery.NoPlayer, announcesRefusal)));
        return registry;
    }

    /// <summary>Places the entity, then gives it the source: AuraSystem puts a source added to an entity already on the map into the field at once.</summary>
    private static void AddSource(ComponentManager componentManager, int entityId, Vector3Int position, byte auraId, byte strength)
    {
        TestTransforms.Set(componentManager, entityId, new TransformComponent(position, UnitSize));
        componentManager.GetMultiPool<AuraSourceComponent>().Add(entityId, new AuraSourceComponent(auraId, strength));
    }

    /// <summary>
    /// Records the move into the shared buffer and drains it on the next frame. Also clears the
    /// buffer afterward, the same way SystemManager would at the end of a real frame's cycle (see
    /// FrameEventBuffer's own doc comment) -- these tests construct AuraSystem
    /// directly, bypassing SystemManager entirely, so without this the recorded move would still
    /// be sitting in the buffer on every later Update call, getting silently reprocessed instead
    /// of just once.
    /// </summary>
    private void MoveObserverTo(AuraSystem system, FrameEventBuffer<EntityMovedEvent> movedEntities, Vector3Int from, Vector3Int to, int entityId = ObserverEntityId)
    {
        movedEntities.Record(new EntityMovedEvent(entityId, from, to, UnitSize));
        Step(system);
        movedEntities.ClearFrame();
    }

    /// <summary>Moves the entity to its destination, leaves it standing there with a Transform to match, and runs one full tick interval -- long enough for the exposure the move started to tick exactly once.</summary>
    private void EnterAndTick(AuraSystem system, ComponentManager componentManager, FrameEventBuffer<EntityMovedEvent> movedEntities, Vector3Int from, Vector3Int to, int entityId = ObserverEntityId)
    {
        TestTransforms.Set(componentManager, entityId, new TransformComponent(to, UnitSize));
        MoveObserverTo(system, movedEntities, from, to, entityId);
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
    }

    private static int StackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<BurningTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    private static int PoisonStackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<PoisonTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    /// <summary>Exposure is one MultiComponentPool instance per (entity, aura) -- this chain-walks entityId's own instances looking for auraId, the way AuraSystem itself (HasExposure) does.</summary>
    private static bool HasExposure(ComponentManager componentManager, int entityId, byte auraId)
    {
        var exposures = componentManager.GetMultiPool<AuraExposureComponent>();
        for (var denseIndex = exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = exposures.GetNextDenseIndex(denseIndex))
        {
            if (exposures.GetReadonlyByDenseIndex(denseIndex).AuraId == auraId)
            {
                return true;
            }
        }

        return false;
    }

    private static uint NextTickFrameOf(ComponentManager componentManager, int entityId, byte auraId)
    {
        var exposures = componentManager.GetMultiPool<AuraExposureComponent>();
        for (var denseIndex = exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = exposures.GetNextDenseIndex(denseIndex))
        {
            var exposure = exposures.GetReadonlyByDenseIndex(denseIndex);
            if (exposure.AuraId == auraId)
            {
                return exposure.NextTickFrame;
            }
        }

        throw new InvalidOperationException($"No aura {auraId} exposure entry for entity {entityId}.");
    }

    // Every scenario below moves purely along one axis, so Manhattan distance (the metric
    // AuraSystem/AuraGrid actually use) and Chebyshev distance coincide -- these
    // numbers would be identical under either metric. DistanceFalloffTests covers the
    // off-axis case where they diverge.
    [TestMethod]
    [DataRow(0, 8)]
    [DataRow(1, 4)]
    [DataRow(2, 2)]
    [DataRow(3, 1)]
    [DataRow(4, 0)]
    public void StandingInRange_FirstTickGrantsFalloffStacksForStrengthEightSource(int distance, int expectedStacks)
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
    }

    [TestMethod]
    public void TwoOverlappingSources_StacksAreAdditive()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        const int secondSourceEntityId = 101;
        var secondSourcePosition = new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z); // distance 2 from SourcePosition
        AddSource(componentManager, secondSourceEntityId, secondSourcePosition, TestAuras.BurningId, strength: 4);

        // Standing directly on the first source: 8 (distance 0 from source 1) + 1 (distance 2 from source 2, 4 >> 2 == 1).
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(9, StackCountOf(componentManager, ObserverEntityId));
    }

    private static (TerrainRegistry Terrain, ushort GlowingTypeId) CreateGlowingTerrain()
    {
        var terrain = new TerrainRegistry();
        var typeId = terrain.Register(new TerrainDefinition(
            "test:glowing", "Glowing", "", default, "~", default,
            Aura: new TerrainAura(TestAuras.Burning, 8)));
        return (terrain, typeId);
    }

    [TestMethod]
    public void GlowingTerrainPlacedBeforeFirstUpdate_ItsAuraApplies()
    {
        var (terrain, glowingTypeId) = CreateGlowingTerrain();
        var mapQuery = new FakeMapQuery { Bounds = new MapBounds(0, 0, 32, 32, 3) };
        mapQuery.SetTerrain(SourcePosition, glowingTypeId);
        var (system, componentManager, _, movedEntities, _) = Build(terrain: terrain, mapQuery: mapQuery);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z));

        Assert.AreEqual(4, StackCountOf(componentManager, ObserverEntityId));
    }

    [TestMethod]
    public void TerrainChangedToGlowing_ExposesOccupantAlreadyInRange()
    {
        var (terrain, glowingTypeId) = CreateGlowingTerrain();
        var mapQuery = new FakeMapQuery { Bounds = new MapBounds(0, 0, 32, 32, 3) };
        var (system, componentManager, _, movedEntities, eventBus) = Build(terrain: terrain, mapQuery: mapQuery);
        AddSource(componentManager, SourceEntityId, new Vector3Int(30, 30, 0), TestAuras.BurningId, strength: 1);
        var observerPosition = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(observerPosition, UnitSize));
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);

        mapQuery.SetTerrain(SourcePosition, glowingTypeId);
        eventBus.Publish(new TerrainChangedEvent(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, 0, glowingTypeId));

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "The change only starts the exposure.");

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(4, StackCountOf(componentManager, ObserverEntityId));
    }

    [TestMethod]
    public void TerrainChangedFromGlowing_DropsExposureOfOccupantInRange()
    {
        var (terrain, glowingTypeId) = CreateGlowingTerrain();
        var mapQuery = new FakeMapQuery { Bounds = new MapBounds(0, 0, 32, 32, 3) };
        mapQuery.SetTerrain(SourcePosition, glowingTypeId);
        var (system, componentManager, _, movedEntities, eventBus) = Build(terrain: terrain, mapQuery: mapQuery);
        var observerPosition = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(observerPosition, UnitSize));
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);

        mapQuery.SetTerrain(SourcePosition, 0);
        eventBus.Publish(new TerrainChangedEvent(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, glowingTypeId, 0));

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
    }

    [TestMethod]
    public void TerrainUnloading_ItsGlowingTerrainNoLongerApplies()
    {
        var (terrain, glowingTypeId) = CreateGlowingTerrain();
        var mapQuery = new FakeMapQuery { Bounds = new MapBounds(0, 0, 32, 32, 3) };
        mapQuery.SetTerrain(SourcePosition, glowingTypeId);
        var (system, componentManager, _, movedEntities, eventBus) = Build(terrain: terrain, mapQuery: mapQuery);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), new Vector3Int(30, 30, 0));

        eventBus.Publish(new TerrainUnloadingEvent(Neighborhoods.AreaOf(0, 0, 3)));
        mapQuery.SetTerrain(SourcePosition, 0);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(30, 30, 0), new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z));

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
    }

    [TestMethod]
    public void TerrainLoaded_ItsGlowingTerrainApplies()
    {
        var (terrain, glowingTypeId) = CreateGlowingTerrain();
        var mapQuery = new FakeMapQuery { Bounds = new MapBounds(0, 0, 32, 32, 3) };
        var (system, componentManager, _, movedEntities, eventBus) = Build(terrain: terrain, mapQuery: mapQuery);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), new Vector3Int(30, 30, 0));

        mapQuery.SetTerrain(SourcePosition, glowingTypeId);
        Assert.IsTrue(TerrainAuraSources.TryGetAura(terrain, TestAuras.GlowOnlyCatalog(), glowingTypeId, out var aura));
        eventBus.Publish(new TerrainLoadedEvent(Neighborhoods.AreaOf(0, 0, 3), [new TerrainAuraCell(SourcePosition, aura)]));
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(30, 30, 0), new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z));

        Assert.AreEqual(4, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>
    /// Regression test for the reported bug: remaining at the same distance across a full
    /// tick cycle must top the stack count off at the distance-based target, not add the
    /// target amount again on top of it. Adding again every cycle would snowball stacks
    /// toward BurningEffects.MaxStacks while standing near a source (grants 4+/cycle here
    /// while Burning's own independent decay -- not exercised by this system-level test --
    /// only removes 1/cycle), leaving a correspondingly long tail to decay after leaving.
    /// </summary>
    [TestMethod]
    public void RemainingInRange_AtTheSameDistance_DoesNotAddStacksBeyondTheTarget()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        // One more full tick interval -- the exposure ticks exactly once more in here.
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Complements the test above: if something else (BurningSystem's own decay, in the real game) reduced the stack count below the target in between, the aura tops it back up to the target rather than ignoring the shortfall or overshooting past it.</summary>
    [TestMethod]
    public void RemainingInRange_ToppsBackUpToTargetAfterExternalDecay()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        // Simulate BurningSystem's own decay (not exercised by this system-level test) having
        // brought the stack count down since the last aura tick.
        componentManager.GetPackedPool<BurningTimerComponent>().TryUpdate(ObserverEntityId, static (ref BurningTimerComponent t) => t.StackCount = 5);

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Topped back up to the target (8), not added on top of the decayed value (5 + 8 = 13).");
    }

    /// <summary>
    /// Leaving range is *not* an immediate event for the exposure timer -- only Update's
    /// scheduled tick decides removal (see AuraSystem's own doc comment). Moving
    /// away must not touch the exposure at all; ObserverLeavesBeforeItsTick_ExposureEndsAndNothingIsApplied
    /// covers the eventual removal once the timer actually ticks.
    /// </summary>
    [TestMethod]
    public void ObserverWalksOutOfRange_ExposureIsNotRemovedImmediately()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
    }

    /// <summary>The tick-only rule's other half: an entity that came into range and left again before its tick is never affected -- the tick finds it out of range, ends the exposure and applies nothing.</summary>
    [TestMethod]
    public void ObserverLeavesBeforeItsTick_ExposureEndsAndNothingIsApplied()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);
        // Update reads the observer's *current* Transform.Position, independent of the
        // EntityMovedEvent itself -- must reflect where it actually ended up.
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(farAwayPosition, UnitSize));

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>
    /// The bug this was written to catch: stepping out of an aura and back in before the
    /// timer's scheduled tick must apply nothing and must not restart the
    /// countdown -- unlike TerrainContactSystem, which deliberately re-triggers on every step.
    /// </summary>
    [TestMethod]
    public void MovingOutAndBackInBeforeTheTick_AppliesNothingAndDoesNotResetTheTimer()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "Entering range applies nothing.");
        var firstTickFrame = NextTickFrameOf(componentManager, ObserverEntityId, TestAuras.BurningId);
        Assert.AreEqual(FrameDeadline.AfterStaggered(_clock.CurrentFrame, AuraEffects.TickIntervalFrames, ObserverEntityId), firstTickFrame, "The first tick is on the observer's staggered deadline after entry.");
        var firstTickDelay = (int)(firstTickFrame - _clock.CurrentFrame);

        RunFrames(system, movedEntities, firstTickDelay / 2);

        // Step out (still in range at distance 1) and back in, all before the timer ticks.
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
        MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "Stepping out and back in before the timer ticks must apply nothing.");
        Assert.AreEqual(firstTickFrame, NextTickFrameOf(componentManager, ObserverEntityId, TestAuras.BurningId), "...nor reset the timer.");

        RunFrames(system, movedEntities, firstTickDelay - firstTickDelay / 2);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "The original timer's tick applies from the entity's current (in-range) position.");
    }

    [TestMethod]
    public void RepeatedEntryExitCycles_NeverThrowsAndNeverDuplicatesExposure()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        for (var i = 0; i < 10; i++)
        {
            MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
            MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        }

        // MultiComponentPool.Add allows several instances per entity (one per aura) but
        // this scenario only ever has one aura in play, so a count of 1 still proves no
        // duplicate Burning entry was ever created for the same entity along the way.
        Assert.AreEqual(1, componentManager.GetMultiPool<AuraExposureComponent>().Count);
    }

    /// <summary>
    /// The scenario Question 4 (from the original design review) asked about directly: if the
    /// source (not the observer) moves away, a stationary observer's exposure must still be
    /// cleared, not left stuck forever. Pinned to Local -- see Part 2's own doc comment on
    /// AuraSystem: a non-Local source's grid resync is now deferred to a periodic
    /// catch-up pass, and this test is about the resync/removal happening at all, not about tier
    /// throttling.
    /// </summary>
    [TestMethod]
    public void SourceMovesAwayFromStationaryObserver_ExposureRemoved()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        mapQuery.SetOccupant(SourcePosition, SourceEntityId); // A moving source is an occupant, not terrain.

        // Observer stands one tile away (two Blocking occupants can't share a cell) and stays
        // put from here on -- its own TransformComponent reflects its resting position, and its
        // occupancy is registered in the fake map the same way WorldEventSync would register it
        // in the real game, since ReEvaluateExposuresNear has to be able to find it via a box
        // scan around the *source's* old/new position, not the observer's own movement.
        var observerPosition = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(observerPosition, UnitSize));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));

        // The source itself moves far away -- the observer never moves again. Transform is
        // updated to the new position first, mirroring EntityMovedEvent's own contract ("Position
        // is already updated by the time this fires").
        mapQuery.ClearOccupant(SourcePosition);
        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        mapQuery.SetOccupant(farAwayPosition, SourceEntityId);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(farAwayPosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition, SourceEntityId);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
    }

    [TestMethod]
    public void SourceDoesNotIgniteItself()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        mapQuery.SetOccupant(SourcePosition, SourceEntityId);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, SourceEntityId));
        Assert.IsFalse(HasExposure(componentManager, SourceEntityId, TestAuras.BurningId));
    }

    /// <summary>
    /// A stacking status effect's aura can grant *any* status effect, harmful or beneficial --
    /// StatusEffectStackAuraEffect goes through StatusEffectApplierRegistry, not a Burning-only special case.
    /// Poison (a debuff, like Burning, but with an entirely different stacking/decay model --
    /// duration-based, all-stacks-expire-together, see PoisonSystem) proves the dispatch is
    /// genuinely generic, not just "one other hardcoded case."
    /// </summary>
    [TestMethod]
    [DataRow(0, 8)]
    [DataRow(1, 4)]
    [DataRow(2, 2)]
    [DataRow(3, 1)]
    [DataRow(4, 0)]
    public void PoisonEffectType_GrantsFalloffStacksViaTheSameGenericDispatchAsBurning(int distance, int expectedStacks)
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, PoisonStackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, HasExposure(componentManager, ObserverEntityId, TestAuras.PoisonId));
    }

    /// <summary>A corpse doesn't accumulate new stacks from a nearby aura source -- see DeathSystem/DeadComponent.</summary>
    [TestMethod]
    public void DeadObserver_InRange_IsNeverExposed()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        componentManager.GetPackedPool<DeadComponent>().Add(ObserverEntityId, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
    }

    /// <summary>Nothing remembers that an aura's effects can't do anything to an entity: it holds an exposure like anything else in range, and each tick simply lands nothing -- so the day the effects can land (an applier registered, an immunity gone), the next tick applies them.</summary>
    [TestMethod]
    public void EffectsThatCannotLandOnTheEntity_HoldAnExposureAndApplyNothing()
    {
        var (system, componentManager, _, movedEntities, _) = Build(new StatusEffectApplierRegistry());
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.PoisonId));
        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>An aura's stacks are attributed to the aura itself: the field holds a total per cell, not which source contributed.</summary>
    [TestMethod]
    public void AuraStacks_AreAttributedToTheAura()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(ActionSource.FromAura(TestAuras.PoisonId), componentManager.GetPackedPool<PoisonTimerComponent>().GetReadonly(ObserverEntityId).Source);
    }

    /// <summary>An entity immune to what an aura grants is told so once for the whole stay, not on every tick, and holds the refusal on its exposure.</summary>
    [TestMethod]
    public void ImmuneEntityStandingInAnAura_IsReportedOncePerStay()
    {
        var floatingText = new TestFloatingText().Place(ObserverEntityId, ProcessingTierLevel.Local);
        var (system, componentManager, _, movedEntities, _) = Build(floatingTextFeed: floatingText.Feed);
        StatusEffectImmunityEffects.GrantPermanent(componentManager.GetMultiPool<StatusEffectImmunityComponent>(), ObserverEntityId, StatusEffectType.Poison);
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        RunFrames(system, movedEntities, 4 * AuraEffects.TickIntervalFrames);

        Assert.AreEqual(1, floatingText.Published.Count(text => text.Kind == FloatingTextKind.Immune));
        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));
        var exposures = componentManager.GetMultiPool<AuraExposureComponent>();
        Assert.IsTrue(exposures.GetReadonlyByDenseIndex(exposures.GetFirstDenseIndex(ObserverEntityId)).Refused);
    }

    /// <summary>The refusal is forgotten as soon as something lands, so a later immunity is reported again.</summary>
    [TestMethod]
    public void ImmunityLost_TheNextTickLandsAndClearsTheRefusal()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        StatusEffectImmunityEffects.GrantPermanent(immunities, ObserverEntityId, StatusEffectType.Poison);
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        immunities.Remove(ObserverEntityId);
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.AreEqual(8, PoisonStackCountOf(componentManager, ObserverEntityId));
        var exposures = componentManager.GetMultiPool<AuraExposureComponent>();
        Assert.IsFalse(exposures.GetReadonlyByDenseIndex(exposures.GetFirstDenseIndex(ObserverEntityId)).Refused);
    }

    private static readonly Guid CustomAuraGuid = new("00000000-0000-0000-0000-0000000000c1");

    /// <summary>An aura holds the same effect lists an action does: here direct damage, scaled by the aura's strength where the entity stands, with no source entity so it never crits.</summary>
    [TestMethod]
    public void AuraHoldingDirectDamage_DamagesByItsStrengthAtTheEntity()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        var auraId = _auras.Register(new AuraDefinition(CustomAuraGuid, "Scorch", Color.Red, [new Effect([new DirectDamage(1, 1)])]));
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(ObserverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        AddSource(componentManager, SourceEntityId, SourcePosition, auraId, strength: 8);
        var beside = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), beside);

        Assert.AreEqual(96, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(ObserverEntityId).CurrentHealth);
    }

    /// <summary>A Flat aura applies its effects at their own amounts wherever it reaches, however strong it is there.</summary>
    [TestMethod]
    public void FlatAura_AppliesItsEffectsUnscaled()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        var auraId = _auras.Register(new AuraDefinition(CustomAuraGuid, "Chill", Color.Blue, [new Effect([new DirectDamage(3, 3)])], Magnitude: AuraMagnitude.Flat));
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(ObserverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        AddSource(componentManager, SourceEntityId, SourcePosition, auraId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(97, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(ObserverEntityId).CurrentHealth);
    }

    /// <summary>An aura granting a refreshing modifier holds one on whoever stands in it, however many ticks pass.</summary>
    [TestMethod]
    public void AuraHoldingARefreshingModifier_HoldsOneOnWhoeverStandsInIt()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        var auraId = _auras.Register(new AuraDefinition(CustomAuraGuid, "Ward", Color.Blue,
            [new Effect([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, DurationFrames: 600, Stacking: StatModifierStacking.RefreshFromSameSource)])],
            Magnitude: AuraMagnitude.Flat));
        AddSource(componentManager, SourceEntityId, SourcePosition, auraId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        RunFrames(system, movedEntities, 3 * AuraEffects.TickIntervalFrames);

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        Assert.AreEqual(1, statModifiers.CountForEntity(ObserverEntityId));
        Assert.AreEqual(ActionSource.FromAura(auraId), statModifiers.GetReadonlyByDenseIndex(statModifiers.GetFirstDenseIndex(ObserverEntityId)).Source);
    }

    /// <summary>The effects are read from the definition at every tick, so replacing the definition changes what the next tick does to whoever is already exposed.</summary>
    [TestMethod]
    public void DefinitionReplacedWithDifferentEffects_TheNextTickAppliesTheNewOnes()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        var auraId = _auras.Register(new AuraDefinition(CustomAuraGuid, "Scorch", Color.Red, [new Effect([new DirectDamage(1, 1)])], Magnitude: AuraMagnitude.Flat));
        componentManager.GetPackedPool<SimpleHealthComponent>().Add(ObserverEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        AddSource(componentManager, SourceEntityId, SourcePosition, auraId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        _auras.Register(new AuraDefinition(CustomAuraGuid, "Scorch", Color.Red, [new Effect([new DirectDamage(10, 10)])], Magnitude: AuraMagnitude.Flat));
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.AreEqual(89, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(ObserverEntityId).CurrentHealth);
    }

    /// <summary>A glow-only aura exposes nothing. Once its definition gains effects, whoever already stands in its reach is exposed without having to move.</summary>
    [TestMethod]
    public void DefinitionGainsEffects_WhoeverStandsInReachIsExposed()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);
        var auraId = _auras.Register(new AuraDefinition(CustomAuraGuid, "Scorch", Color.Red));
        AddSource(componentManager, SourceEntityId, SourcePosition, auraId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, auraId), "Precondition: nothing is exposed to an aura with no effects.");

        _auras.Register(new AuraDefinition(CustomAuraGuid, "Scorch", Color.Red, [new Effect([new StatusEffectGrant(StatusEffectType.Poison, 1, StatusEffectGrantMode.TopUpTo)])]));

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, auraId));
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(8, PoisonStackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>An aura whose definition loses its effects stops applying, and its exposures end on their next tick.</summary>
    [TestMethod]
    public void DefinitionLosesItsEffects_ExposuresEndOnTheirNextTick()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.PoisonId, strength: 8);
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        _auras.Register(TestAuras.PoisonGlowOnly);
        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.PoisonId));
    }

    /// <summary>An aura with no effect is in the grid for its glow only: nothing is ever exposed to it.</summary>
    [TestMethod]
    public void GlowOnlyAura_ExposesNothing()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.LightId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, componentManager.GetMultiPool<AuraExposureComponent>().Count);
    }

    /// <summary>
    /// Exposures are not ProcessingTier-gated -- they sit on a timer wheel and tick on
    /// their exact frame at every tier. Beyond is the coarsest tier. Sets up an existing exposure
    /// directly, with a deadline of its own choosing.
    /// </summary>
    [TestMethod]
    [DataRow(ProcessingTierLevel.Local)]
    [DataRow(ProcessingTierLevel.Beyond)]
    public void ExposureAtAnyTier_TicksOnItsExactFrame(ProcessingTierLevel tier)
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(tier));
        componentManager.GetMultiPool<AuraExposureComponent>().Add(ObserverEntityId, new AuraExposureComponent(TestAuras.BurningId, nextTickFrame: 7));

        RunFrames(system, movedEntities, 6);
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "Not yet due.");

        RunFrames(system, movedEntities, 1);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(7u + AuraEffects.TickIntervalFrames, NextTickFrameOf(componentManager, ObserverEntityId, TestAuras.BurningId), "Re-armed one interval after its tick.");
    }

    /// <summary>A source toggled on after the field is built, as every real toggle-on is, must reach the field, so an observer walking onto it is affected.</summary>
    [TestMethod]
    public void SourceAddedViaToggleAfterGridAlreadyBuilt_ObserverMovingOntoItIsAffected()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built"
        // by the time the toggle below happens.
        RunFrames(system, movedEntities, 1);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Mirrors the Added case above -- a source toggled off after the grid is already built must retract its own contribution, not leave a permanent ghost entry future observers keep getting credited (or debited) for.</summary>
    [TestMethod]
    public void SourceRemovedViaToggleAfterGridAlreadyBuilt_LaterObserverMovingOntoItIsNotAffected()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);
        Assert.IsFalse(sourcePool.Has(SourceEntityId));

        // A second, different observer walking onto the same tile afterward proves the grid's
        // own contribution was actually retracted -- not just that the first observer's
        // exposure happened to clear.
        const int secondObserverEntityId = 150;
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, secondObserverEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, secondObserverEntityId));
    }

    /// <summary>
    /// The multi-aura regression: an entity carrying two DIFFERENT effect-type sources must have
    /// both chain-walked by OnEntityMoved when it moves, not just whichever single instance an
    /// old TryGetReadonly would have picked up. SourceEntityId is pinned to Local -- see Part 2's
    /// own doc comment: a non-Local source's grid resync is now deferred to a periodic catch-up
    /// pass, and this test is specifically about the move's own resync reaching AuraGrid, not
    /// about tier throttling.
    /// </summary>
    [TestMethod]
    public void SourceWithTwoAuras_MovingOntoAlreadyExposedObserver_AppliesBoth()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();

        var observerPosition = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        AddSource(componentManager, entityId: 150, observerPosition, TestAuras.PoisonId, strength: 1);
        // Pinned to Local so the tick loop below reliably reaches it -- an entity with no
        // ProcessingTierComponent yet fails open to Beyond, the slowest cadence (see
        // ProcessingTierWiring's own doc comment), which a single TickIntervalFrames loop
        // wouldn't otherwise catch.
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        sourcePool.Add(SourceEntityId, new AuraSourceComponent(TestAuras.BurningId, strength: 8));
        sourcePool.Add(SourceEntityId, new AuraSourceComponent(TestAuras.PoisonId, strength: 8));

        // Establishes the observer's exposure (via the weak anchor Poison source) -- EnsureGrid's
        // bulk scatter, triggered by this same call, also picks up the dual-typed source's own
        // two instances, since both were placed before this very first Update (exactly how
        // population would for real) -- this doesn't exercise the reactive Added path.
        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        // Registered as a real map occupant -- StartExposuresNear's box scan (triggered by the
        // SOURCE's own move below) needs to actually find it, the same way every other
        // "stationary occupant gets granted immediately" test already registers its own occupant.
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);
        Assert.AreEqual(1, PoisonStackCountOf(componentManager, ObserverEntityId));

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(observerPosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, observerPosition, SourceEntityId);

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Burning contribution from the moved dual-typed source must register in the grid.");
        Assert.AreEqual(9, PoisonStackCountOf(componentManager, ObserverEntityId), "Poison from both sources (1 + 8) is additive -- the moved source's own Poison instance is correctly chain-walked too, not just its Burning one.");
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.PoisonId));
    }

    /// <summary>TotalStrengthExcludingSelf must sum ALL of the entity's own sources of the aura, not just the first one a single-source check would have found.</summary>
    [TestMethod]
    public void SelfExclusion_EntityWithTwoSameTypeSources_ExcludesBothFromOwnReading()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        sourcePool.Add(SourceEntityId, new AuraSourceComponent(TestAuras.BurningId, strength: 8));
        sourcePool.Add(SourceEntityId, new AuraSourceComponent(TestAuras.BurningId, strength: 4));
        mapQuery.SetOccupant(SourcePosition, SourceEntityId);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, SourceEntityId), "Both of the source's own instances must be excluded from its own reading, not just the first one found.");
        Assert.IsFalse(HasExposure(componentManager, SourceEntityId, TestAuras.BurningId));
    }

    /// <summary>A stationary target already standing where an aura is toggled on must be exposed at once, not have to wait until it happens to move; the aura then applies on that exposure's tick.</summary>
    [TestMethod]
    public void SourceAddedViaToggle_StationaryOccupantAlreadyInRange_ExposedImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built" by the time the toggle below happens, same setup as the sync-bug regression tests above.
        RunFrames(system, movedEntities, 1);

        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "A stationary target already in range must be exposed the moment the aura toggles on.");
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>
    /// Regression test for the fix: StartExposuresNear/ReEvaluateExposuresNear used to scan
    /// IMapQuery.GetEntityIdsInBox, which only ever reports the Blocking occupant of a cell --
    /// a Tiny/Phasing (non-Blocking) occupant sharing that cell was silently invisible to
    /// auras. ObserverEntityId here is registered ONLY via SetNonBlockingOccupant (FakeMapQuery's
    /// non-Blocking index), never via SetOccupant/GetEntityIdAt, so this fails against the old
    /// GetEntityIdsInBox-based scan and passes only once the scan goes through
    /// GetOccupantEntityIdsAt instead.
    /// </summary>
    [TestMethod]
    public void SourceAddedViaToggle_StationaryNonBlockingOccupantAlreadyInRange_ExposedImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built" by the time the toggle below happens, same setup as the sync-bug regression tests above.
        RunFrames(system, movedEntities, 1);

        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetNonBlockingOccupant(SourcePosition, ObserverEntityId);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "A stationary non-Blocking (e.g. Tiny/Phasing) occupant already in range must be exposed too, not just Blocking ones.");

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Mirrors the Added case above: toggling off must immediately clear a stationary nearby occupant's exposure, not leave it lingering until that occupant's own next scheduled tick.</summary>
    [TestMethod]
    public void SourceRemovedViaToggle_StationaryOccupantInRange_ExposureClearedImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        // ReEvaluateExposuresNear needs a real Transform to find the occupant at all (MoveObserverTo only records the move event, it doesn't also write the mover's own Transform).
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));

        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "Toggling off must immediately re-check nearby exposures, not wait for the observer's own next tick.");
    }

    /// <summary>
    /// The realistic version of the reported bug: the aura is already toggled on (not the exact
    /// moment of toggling), and the SOURCE -- not the target -- is the one that moves up to a
    /// stationary occupant. OnEntityMoved's own re-check pass used to be removal-only; walking an
    /// already-active aura up to someone must grant them immediately too, not just stop affecting
    /// whoever it walks away from. Pinned to Local -- see Part 2's own doc comment: only a
    /// Local-tier source resyncs synchronously on every move; this test is about that resync
    /// reaching a stationary occupant immediately, not about tier throttling (see the
    /// SourceAtNonLocalTier_* tests below for that).
    /// </summary>
    [TestMethod]
    public void SourceCarryingActiveAura_MovesOntoStationaryOccupant_ExposesImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        // Toggle the aura on far away from the eventual target, forcing EnsureGrid to run first.
        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        RunFrames(system, movedEntities, 1);
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        // A stationary occupant standing where the source is about to walk to -- never itself moves.
        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "A stationary occupant the source walks up to must be exposed immediately, not wait for it to move itself.");

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>
    /// Part 2's own behavior change: a moving source that is NOT Local-tiered (Neighborhood
    /// here) must not resync its grid contribution synchronously on the spot -- see
    /// AuraSystem.ResyncSourceIfStale's own doc comment on why (an O(radius^2)
    /// resync per move is fine for a rare Local case, not for a whole population of far-away
    /// moving auras). Same setup as SourceCarryingActiveAura_MovesOntoStationaryOccupant_
    /// GrantsImmediately, but Neighborhood-tiered instead of Local, and expects the OPPOSITE
    /// outcome from a single move/Update call.
    /// </summary>
    [TestMethod]
    public void SourceAtNonLocalTier_MovingOntoStationaryOccupant_DoesNotExposeSynchronously()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        RunFrames(system, movedEntities, 1);
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "A non-Local source's grid resync must not happen synchronously on the move itself.");
    }

    /// <summary>Complements the test above: the deferred resync isn't lost, just delayed -- Update's own periodic catch-up pass (driven by the source's own tiered cadence) must eventually reach it and grant the stationary occupant.</summary>
    [TestMethod]
    public void SourceAtNonLocalTier_EventuallyResyncsViaPeriodicCatchUp()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        RunFrames(system, movedEntities, 1);
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId, strength: 8);

        TestTransforms.Set(componentManager, ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);
        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId), "Sanity check: still not resynced immediately after the move itself.");

        RunFrames(system, movedEntities, GenerousCatchUpFrameCount(system) + AuraEffects.TickIntervalFrames);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "The periodic catch-up pass must eventually resync a non-Local source's stale grid contribution, exposing the stationary occupant.");
    }

    /// <summary>
    /// Exposure is per aura, never one flag for "in range of something": an occupant already
    /// exposed to one aura (Burning here) must be exposed to a newly toggled-on second one (Poison)
    /// at once, the same guarantee
    /// SourceAddedViaToggle_StationaryOccupantAlreadyInRange_ExposedImmediately covers for the
    /// zero-prior-exposure case.
    /// </summary>
    [TestMethod]
    public void SourceAddedViaToggle_OccupantAlreadyExposedToADifferentAura_ExposedToTheNewOneImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));

        const int secondSourceEntityId = 150;
        TestTransforms.Set(componentManager, secondSourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, secondSourceEntityId, TestAuras.PoisonId, strength: 8);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.PoisonId), "Already being exposed to Burning must not block exposure to the newly-toggled Poison source.");

        RunFrames(system, movedEntities, AuraEffects.TickIntervalFrames);
        Assert.AreEqual(8, PoisonStackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>
    /// The mechanism population-time placement relies on (see FloorBuilder.CreatePlayer's own
    /// "spawning counts as a move" comment, and TestMapBuilder's own equivalent for the rest of
    /// the population): a synthetic EntityMovedEvent with OldPosition == NewPosition, recorded
    /// once at placement time since World.PlaceEntityOnMap itself never raises one, must still
    /// read as a genuine fresh entry -- an entity placed directly into a static aura's range,
    /// never having actually stepped there, must still be granted immediately rather than only
    /// on whatever move it happens to make next under its own power.
    /// </summary>
    [TestMethod]
    public void SpawnPositionEqualsOldPosition_StillStartsAnExposure()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, SourcePosition, SourcePosition);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, TestAuras.BurningId));
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>An exposure doesn't accrue while its entity is frozen: owed re-grants are skipped, not granted, and the next one keeps the exposure's cadence.</summary>
    [TestMethod]
    public void EntityResumed_OwedExposureRegrants_AreSkippedNotGranted()
    {
        var scope = new SimulationScope(static _ => false);
        var (_, componentManager, _, _, _) = Build(simulationScope: scope);
        componentManager.GetMultiPool<AuraExposureComponent>().Add(ObserverEntityId, new AuraExposureComponent(TestAuras.BurningId, nextTickFrame: 60));
        _clock.Advance(630);

        scope.RaiseResumed(ObserverEntityId);

        Assert.AreEqual(660u, NextTickFrameOf(componentManager, ObserverEntityId, TestAuras.BurningId));
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
    }

    [TestMethod]
    public void FirstTickInRange_PublishesTheStacksAdded()
    {
        var floatingText = new TestFloatingText().Place(ObserverEntityId, ProcessingTierLevel.Local, SourcePosition.X, SourcePosition.Y);
        var (system, componentManager, _, movedEntities, _) = Build(floatingTextFeed: floatingText.Feed);
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        var published = floatingText.Published.Single();
        Assert.AreEqual(FloatingTextKind.StatusEffectStacksAdded, published.Kind);
        Assert.AreEqual(StatusEffectType.Burning, published.EffectType);
        Assert.AreEqual(8, published.Amount);
    }

    [TestMethod]
    public void FirstTickInRange_Immune_PublishesImmune()
    {
        var floatingText = new TestFloatingText().Place(ObserverEntityId, ProcessingTierLevel.Local, SourcePosition.X, SourcePosition.Y);
        var (system, componentManager, _, movedEntities, _) = Build(floatingTextFeed: floatingText.Feed);
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        componentManager.GetMultiPool<Game.Modules.StatusEffects.Components.StatusEffectImmunityComponent>().Add(ObserverEntityId, new Game.Modules.StatusEffects.Components.StatusEffectImmunityComponent(StatusEffectType.Burning, uint.MaxValue));

        EnterAndTick(system, componentManager, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(FloatingTextKind.Immune, floatingText.Published.Single().Kind);
    }

    /// <summary>The blueprint case: a source is on the entity before the entity is on the map, and nothing announces it. The spawn's move puts it into the field at once, whatever the entity's tier -- a shrine streamed in at the frozen edge of the window is in the field from its spawn.</summary>
    [TestMethod]
    [DataRow(ProcessingTierLevel.Local)]
    [DataRow(ProcessingTierLevel.Borough)]
    public void SourceBuiltBeforePlacement_SpawnMovePutsItInTheFieldAtAnyTier(ProcessingTierLevel tier)
    {
        var unplaced = new Vector3Int(-5000, -5000, 0);
        var (system, componentManager, _, movedEntities, _) = Build(mapQuery: new FakeMapQuery { OffMapPosition = unplaced });
        RunFrames(system, movedEntities, 1);
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(tier));
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(unplaced, UnitSize));
        componentManager.GetMultiPool<AuraSourceComponent>().Add(SourceEntityId, new AuraSourceComponent(TestAuras.BurningId, strength: 8));
        Assert.AreEqual(0, _auraField.GetTotalStrengthAt(unplaced, TestAuras.BurningId), "Nothing goes into the field for an entity that isn't on the map.");

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, SourcePosition, SourceEntityId);

        Assert.AreEqual(8, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
        Assert.IsTrue(_auraField.TryGetGlow(SourcePosition, out _, out _));
    }

    /// <summary>A source added straight to the pool of an entity already on the map -- a blueprint applied to a live entity -- needs no event to reach the field.</summary>
    [TestMethod]
    public void SourceAddedDirectlyToThePool_OnAPlacedEntity_IsInTheFieldAtOnce()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        RunFrames(system, movedEntities, 1);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));

        componentManager.GetMultiPool<AuraSourceComponent>().Add(SourceEntityId, new AuraSourceComponent(TestAuras.BurningId, strength: 8));

        Assert.AreEqual(8, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
    }

    /// <summary>A non-Local source that moved is still in the field where it was. Removing it must take it out from there, not from where the entity stands now -- which would leave the old reach behind for good and cut a hole in the new one.</summary>
    [TestMethod]
    public void SourceRemovedAfterAnUnsyncedMove_LeavesTheFieldFromWhereItWas()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        var movedTo = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(movedTo, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, movedTo, SourceEntityId);
        Assert.AreEqual(8, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId), "Precondition: the move has not been resynced yet.");

        AuraSourceEffects.Revoke(componentManager.GetMultiPool<AuraSourceComponent>(), eventBus, SourceEntityId, TestAuras.BurningId);

        Assert.AreEqual(0, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
        Assert.AreEqual(0, _auraField.GetTotalStrengthAt(movedTo, TestAuras.BurningId));
    }

    /// <summary>Toggle on, walk, toggle off: the glow follows the carrier and nothing is left at either end.</summary>
    [TestMethod]
    public void LocalSourceTogglesOnThenMovesThenTogglesOff_GlowFollowsAndLeavesNothingBehind()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(SourcePosition, UnitSize));

        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.PoisonId, strength: 8);
        Assert.IsTrue(_auraField.TryGetGlow(SourcePosition, out var glowColor, out _));
        Assert.AreEqual(Color.DarkGreen, glowColor);

        var movedTo = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(movedTo, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, movedTo, SourceEntityId);
        Assert.IsFalse(_auraField.TryGetGlow(SourcePosition, out _, out _), "The glow must leave the old position.");
        Assert.IsTrue(_auraField.TryGetGlow(movedTo, out _, out _));

        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.PoisonId, strength: 8);

        Assert.IsFalse(_auraField.TryGetGlow(SourcePosition, out _, out _));
        Assert.IsFalse(_auraField.TryGetGlow(movedTo, out _, out _));
    }

    /// <summary>A second aura added to an entity the field already holds joins the first where the field has it, and each is removed on its own.</summary>
    [TestMethod]
    public void SecondAuraOnOneSource_JoinsTheFirstAndIsRemovedIndependently()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();
        var sourcePool = componentManager.GetMultiPool<AuraSourceComponent>();
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);
        RunFrames(system, movedEntities, 1);

        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, TestAuras.PoisonId, strength: 4);
        Assert.AreEqual(8, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
        Assert.AreEqual(4, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.PoisonId));

        AuraSourceEffects.Revoke(sourcePool, eventBus, SourceEntityId, TestAuras.BurningId);

        Assert.AreEqual(0, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
        Assert.AreEqual(4, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.PoisonId));
    }

    /// <summary>A source whose entity leaves the map takes its reach with it.</summary>
    [TestMethod]
    public void LocalSourceLeavesTheMap_ItsReachIsLifted()
    {
        var unplaced = new Vector3Int(-5000, -5000, 0);
        var (system, componentManager, _, movedEntities, _) = Build(mapQuery: new FakeMapQuery { OffMapPosition = unplaced });
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        AddSource(componentManager, SourceEntityId, SourcePosition, TestAuras.BurningId, strength: 8);

        TestTransforms.Set(componentManager, SourceEntityId, new TransformComponent(unplaced, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, unplaced, SourceEntityId);

        Assert.AreEqual(0, _auraField.GetTotalStrengthAt(SourcePosition, TestAuras.BurningId));
    }
}
