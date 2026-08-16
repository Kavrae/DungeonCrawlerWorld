using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffectAura.Systems;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.StatusEffectAura;

[TestClass]
public sealed class StatusEffectAuraSystemTests
{
    private const int SourceEntityId = 100;
    private const int ObserverEntityId = 0;

    private static readonly Vector3Int SourcePosition = new(10, 10, 0);
    private static readonly Vector2Byte UnitSize = new(1, 1);

    /// <summary>
    /// Minimal IMapQuery test double with a real per-cell occupant dictionary -- backs
    /// StatusEffectAuraSystem's rare "a source moved" re-check path (GrantToOccupantsNear/
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

        public Vector3Int MapSize { get; } = new(1000, 1000, 3);
        public bool IsOnMap(Vector3Int position) => true;
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
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;

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
    /// Update's periodic re-check pass is now gated by EngineTime.FrameCount (via
    /// TieredEntityStripeSet), not by the stripeIndex parameter -- stripeIndex is accepted for
    /// ISystem compliance but otherwise unused. A FrameCount of 1 never lands ObserverEntityId
    /// (0, Local-tiered, StripeCount 15) on its own due bucket (0), so draining a just-recorded
    /// move doesn't also consume one of the mover's own real tick opportunities that same call --
    /// existing rotating-FrameCount loops elsewhere in these tests keep landing on the same tick
    /// counts they always did.
    /// </summary>
    private const long DrainOnlyFrameCount = 1;

    /// <summary>Comfortably covers a full cycle of every tier's own cadence (base StripeCount * the coarsest divisor, 8, times two) regardless of which bucket a given entityId happens to land in, without needing to compute the exact modulo -- used by tests proving the periodic catch-up pass (as opposed to an instant, same-call resync) eventually reaches a non-Local source.</summary>
    private static int GenerousCatchUpFrameCount(StatusEffectAuraSystem system) => system.StripeCount * 8 * 2;

    /// <summary>Mirrors real game wiring (both BurningModule.Configure and PoisonModule.Configure registering their own applier into the same shared registry) -- the registry a caller can override via applierRegistry to exercise unsupported-effect-type behavior instead.</summary>
    private static (StatusEffectAuraSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, FrameEventBuffer<EntityMovedEvent> MovedEntities, EventBus EventBus) Build(StatusEffectAuraApplierRegistry? applierRegistry = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 200, initialComponentCapacity: 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectAuraSourceComponent>();
        componentManager.RegisterMultiPool<StatusEffectAuraExposureComponent>();
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();
        var eventBus = new EventBus();

        var system = new StatusEffectAuraSystem(
            componentManager,
            componentManager.GetMultiPool<StatusEffectAuraExposureComponent>(),
            componentManager.GetMultiPool<StatusEffectAuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            mapQuery,
            eventBus,
            applierRegistry ?? DefaultApplierRegistry(),
            movedEntities,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            new ProcessingTierEvents(),
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, movedEntities, eventBus);
    }

    private static StatusEffectAuraApplierRegistry DefaultApplierRegistry()
    {
        var registry = new StatusEffectAuraApplierRegistry();
        registry.Register(new TimerBasedAuraApplier<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.ApplyStack));
        registry.Register(new TimerBasedAuraApplier<PoisonTimerComponent>(StatusEffectType.Poison, (cm, id, source) => PoisonEffects.ApplyStack(cm, id, source, durationInTicks: 1)));
        return registry;
    }

    /// <summary>AuraGrid only finds a source by scanning its TransformComponent (see StatusEffectAuraSystem.EnsureGrid) -- a source needs one set at its real position for any of these tests to see it, exactly as PlaceTerrainOnMap/PlaceEntityOnMap would set it for real in-game.</summary>
    private static void AddSource(ComponentManager componentManager, int entityId, Vector3Int position, StatusEffectType effectType, byte strength)
    {
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(entityId, new StatusEffectAuraSourceComponent(effectType, strength, Color.Orange));
        componentManager.Merge(entityId, new TransformComponent(position, UnitSize));
    }

    /// <summary>
    /// Records the move into the shared buffer and immediately drains it via a FrameCount that
    /// can't touch the mover's own exposure timer -- see DrainOnlyFrameCount's own doc comment
    /// for why this doesn't consume a real tick opportunity. Also clears the buffer afterward,
    /// the same way SystemManager would at the end of a real frame's cycle (see FrameEventBuffer's
    /// own doc comment) -- these tests construct StatusEffectAuraSystem directly, bypassing
    /// SystemManager entirely, so without this the recorded move would still be sitting in the
    /// buffer on every later Update call in these tests' own tick loops, getting silently
    /// reprocessed (re-detecting the same move, over and over) instead of just once.
    /// </summary>
    private static void MoveObserverTo(StatusEffectAuraSystem system, FrameEventBuffer<EntityMovedEvent> movedEntities, Vector3Int from, Vector3Int to, int entityId = ObserverEntityId)
    {
        movedEntities.Record(new EntityMovedEvent(entityId, from, to, UnitSize));
        system.Update(new EngineTime(default, default, false, FrameCount: DrainOnlyFrameCount), 0);
        movedEntities.ClearFrame();
    }

    private static int StackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<BurningTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    private static int PoisonStackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<PoisonTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    /// <summary>Exposure is now one MultiComponentPool instance per (entity, EffectType) -- this chain-walks entityId's own instances looking for effectType, mirroring how StatusEffectAuraSystem itself (HasExposure) and StatusEffectAuraSourceComponent lookups already do it.</summary>
    private static bool HasExposure(ComponentManager componentManager, int entityId, StatusEffectType effectType)
    {
        var exposures = componentManager.GetMultiPool<StatusEffectAuraExposureComponent>();
        for (var denseIndex = exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = exposures.GetNextDenseIndex(denseIndex))
        {
            if (exposures.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                return true;
            }
        }

        return false;
    }

    private static int FramesUntilNextTickOf(ComponentManager componentManager, int entityId, StatusEffectType effectType)
    {
        var exposures = componentManager.GetMultiPool<StatusEffectAuraExposureComponent>();
        for (var denseIndex = exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = exposures.GetNextDenseIndex(denseIndex))
        {
            var exposure = exposures.GetReadonlyByDenseIndex(denseIndex);
            if (exposure.EffectType == effectType)
            {
                return exposure.FramesUntilNextTick;
            }
        }

        throw new InvalidOperationException($"No {effectType} exposure entry for entity {entityId}.");
    }

    // Every scenario below moves purely along one axis, so Manhattan distance (the metric
    // StatusEffectAuraSystem/AuraGrid actually use) and Chebyshev distance coincide -- these
    // numbers would be identical under either metric. DistanceFalloffTests covers the
    // off-axis case where they diverge.
    [TestMethod]
    [DataRow(0, 8)]
    [DataRow(1, 4)]
    [DataRow(2, 2)]
    [DataRow(3, 1)]
    [DataRow(4, 0)]
    public void SteppingIntoRange_GrantsFalloffStacksForStrengthEightSource(int distance, int expectedStacks)
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    [TestMethod]
    public void TwoOverlappingSources_StacksAreAdditive()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        const int secondSourceEntityId = 101;
        var secondSourcePosition = new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z); // distance 2 from SourcePosition
        AddSource(componentManager, secondSourceEntityId, secondSourcePosition, StatusEffectType.Burning, strength: 4);

        // Standing directly on the first source: 8 (distance 0 from source 1) + 1 (distance 2 from source 2, 4 >> 2 == 1).
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(9, StackCountOf(componentManager, ObserverEntityId));
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
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        // Rotates stripeIndex across all of StatusEffectAuraSystem's stripes the same way
        // SystemManager does in real play (see BurningSystemTests' equivalent regression test)
        // -- ObserverEntityId (0) always lands in stripe 0 regardless of StripeCount, so a
        // fixed-stripeIndex loop wouldn't actually exercise striping at all.
        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Complements the test above: if something else (BurningSystem's own decay, in the real game) reduced the stack count below the target in between, the aura tops it back up to the target rather than ignoring the shortfall or overshooting past it.</summary>
    [TestMethod]
    public void RemainingInRange_ToppsBackUpToTargetAfterExternalDecay()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        // Simulate BurningSystem's own decay (not exercised by this system-level test) having
        // brought the stack count down since the last aura tick.
        componentManager.GetPackedPool<BurningTimerComponent>().TryUpdate(ObserverEntityId, static (ref BurningTimerComponent t) => t.StackCount = 5);

        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Topped back up to the target (8), not added on top of the decayed value (5 + 8 = 13).");
    }

    /// <summary>
    /// Leaving range is *not* an immediate event for the exposure timer -- only Update's
    /// scheduled tick decides removal (see StatusEffectAuraSystem's own doc comment). Moving
    /// away must not touch the exposure at all; ObserverWalksOutOfRange_ExposureRemovedOnceTimerTicksWhileStillAway
    /// covers the eventual removal once the timer actually ticks.
    /// </summary>
    [TestMethod]
    public void ObserverWalksOutOfRange_ExposureIsNotRemovedImmediately()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);

        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    [TestMethod]
    public void ObserverWalksOutOfRange_ExposureRemovedOnceTimerTicksWhileStillAway()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);
        // Update reads the observer's *current* Transform.Position, independent of the
        // EntityMovedEvent itself -- must reflect where it actually ended up.
        componentManager.Merge(ObserverEntityId, new TransformComponent(farAwayPosition, UnitSize));

        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    /// <summary>
    /// The bug this was written to catch: stepping out of an aura and back in before the
    /// timer's next scheduled tick must grant nothing extra and must not restart the
    /// countdown -- unlike ContactDamageSystem, which deliberately re-triggers on every step.
    /// </summary>
    [TestMethod]
    public void MovingOutAndBackInBeforeNextTick_DoesNotRegrantOrResetTimer()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        // Pinned to Local so the two 30-frame loops' exact FramesUntilNextTick assertions below
        // (framesPerVisit == base StripeCount, matching AuraEffects.TickIntervalFrames pacing)
        // don't depend on whatever the untiered fail-open default happens to be -- this test is
        // about regrant/reset behavior, not tier throttling.
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(30, FramesUntilNextTickOf(componentManager, ObserverEntityId, StatusEffectType.Burning));

        // Step out (still in range at distance 1 -- but exposure already exists, so this
        // must not grant) and back in, all before the original timer would naturally tick.
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
        MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Stepping out and back in before the timer ticks must not grant again.");
        Assert.AreEqual(30, FramesUntilNextTickOf(componentManager, ObserverEntityId, StatusEffectType.Burning), "...nor reset the timer.");

        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "The original timer reaching 0 re-evaluates based on the entity's current (in-range) position, topping off to the target rather than adding to it again.");
    }

    [TestMethod]
    public void RepeatedEntryExitCycles_NeverThrowsAndNeverDuplicatesExposure()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        for (var i = 0; i < 10; i++)
        {
            MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
            MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        }

        // MultiComponentPool.Add allows several instances per entity (one per EffectType) but
        // this scenario only ever has one type in play, so a count of 1 still proves no
        // duplicate Burning entry was ever created for the same entity along the way.
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(1, componentManager.GetMultiPool<StatusEffectAuraExposureComponent>().Count);
    }

    /// <summary>
    /// The scenario Question 4 (from the original design review) asked about directly: if the
    /// source (not the observer) moves away, a stationary observer's exposure must still be
    /// cleared, not left stuck forever. Pinned to Local -- see Part 2's own doc comment on
    /// StatusEffectAuraSystem: a non-Local source's grid resync is now deferred to a periodic
    /// catch-up pass, and this test is about the resync/removal happening at all, not about tier
    /// throttling.
    /// </summary>
    [TestMethod]
    public void SourceMovesAwayFromStationaryObserver_ExposureRemoved()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        mapQuery.SetOccupant(SourcePosition, SourceEntityId); // A moving source is an occupant, not terrain.

        // Observer stands one tile away (two Blocking occupants can't share a cell) and stays
        // put from here on -- its own TransformComponent reflects its resting position, and its
        // occupancy is registered in the fake map the same way WorldEventSync would register it
        // in the real game, since ReEvaluateExposuresNear has to be able to find it via a box
        // scan around the *source's* old/new position, not the observer's own movement.
        var observerPosition = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        componentManager.Merge(ObserverEntityId, new TransformComponent(observerPosition, UnitSize));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));

        // The source itself moves far away -- the observer never moves again. Transform is
        // updated to the new position first, mirroring EntityMovedEvent's own contract ("Position
        // is already updated by the time this fires").
        mapQuery.ClearOccupant(SourcePosition);
        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        mapQuery.SetOccupant(farAwayPosition, SourceEntityId);
        componentManager.Merge(SourceEntityId, new TransformComponent(farAwayPosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition, SourceEntityId);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    [TestMethod]
    public void SourceDoesNotIgniteItself()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        mapQuery.SetOccupant(SourcePosition, SourceEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, SourceEntityId));
        Assert.IsFalse(HasExposure(componentManager, SourceEntityId, StatusEffectType.Burning));
    }

    /// <summary>
    /// GrantStacks assumes an aura can grant *any* status effect, harmful or beneficial --
    /// dispatch goes through StatusEffectAuraApplierRegistry, not a Burning-only special case.
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
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Poison, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, PoisonStackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, HasExposure(componentManager, ObserverEntityId, StatusEffectType.Poison));
    }

    /// <summary>A corpse doesn't accumulate new stacks from a nearby aura source -- see DeathSystem/DeadComponent.</summary>
    [TestMethod]
    public void DeadObserver_InRange_GrantsNothing()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.GetPackedPool<DeadComponent>().Add(ObserverEntityId, new DeadComponent(KilledByEntityId: null));

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    /// <summary>StatusEffectAuraSourceComponent generalizes over EffectType, but granting still requires some module to have actually registered an IStatusEffectAuraApplier for it (see GrantStacks) -- a source authored for an effect type nothing has registered yet must grant nothing rather than throwing, and since nothing is actually granted, it must not track exposure either. GrantStacks reports "has a registered applier or not" precisely so TryGrantSingleType doesn't infer "in range of something real" from a grid whose effect type nothing can apply.</summary>
    [TestMethod]
    public void EffectTypeWithNoRegisteredApplier_GrantsNothingAndTracksNoExposure()
    {
        var (system, componentManager, _, movedEntities, _) = Build(new StatusEffectAuraApplierRegistry());
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Poison, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));
        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Poison),
            "An effect type with no registered applier grants nothing, so it must not create a phantom exposure either.");
    }

    /// <summary>Only the periodic re-grant pass is ProcessingTier-gated, not the buffer drain -- see Update's own comment. Sets up an existing exposure directly (bypassing MoveObserverTo's fresh-entry grant) to exercise that pass in isolation.</summary>
    [TestMethod]
    public void Update_ThrottledObserver_OffCycle_DoesNotDecrementExposureCountdown()
    {
        var (system, componentManager, _, _, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.GetMultiPool<StatusEffectAuraExposureComponent>().Add(ObserverEntityId, new StatusEffectAuraExposureComponent(StatusEffectType.Burning, AuraEffects.TickIntervalFrames));

        // ObserverEntityId (0), Neighborhood-tiered (StripeCount 15 * divisor 2 = 30), lands in
        // bucket 0 -- due only when FrameCount % 30 == 0.
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(AuraEffects.TickIntervalFrames, FramesUntilNextTickOf(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    [TestMethod]
    public void Update_ThrottledObserver_OnEligibleCycle_DecrementsExposureCountdown()
    {
        var (system, componentManager, _, _, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.GetMultiPool<StatusEffectAuraExposureComponent>().Add(ObserverEntityId, new StatusEffectAuraExposureComponent(StatusEffectType.Burning, AuraEffects.TickIntervalFrames));

        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        // Decremented by the Neighborhood tier's own framesPerVisit (StripeCount 15 * divisor 2 = 30), not the base StripeCount.
        Assert.AreEqual(AuraEffects.TickIntervalFrames - (system.StripeCount * 2), FramesUntilNextTickOf(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    /// <summary>
    /// The core regression test for the reported sync bug: before AuraSourceAddedEvent existed,
    /// a source toggled on after EnsureGrid already ran (i.e. after the grid is "already built",
    /// exactly like every real toggle-on will be -- see the doc comment on why) never reached
    /// AuraGrid at all, so an observer walking directly onto it was granted nothing.
    /// </summary>
    [TestMethod]
    public void SourceAddedViaToggleAfterGridAlreadyBuilt_ObserverMovingOntoItIsGranted()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built"
        // by the time the toggle below happens.
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Mirrors the Added case above -- a source toggled off after the grid is already built must retract its own contribution, not leave a permanent ghost entry future observers keep getting credited (or debited) for.</summary>
    [TestMethod]
    public void SourceRemovedViaToggleAfterGridAlreadyBuilt_LaterObserverMovingOntoItIsNotGranted()
    {
        var (system, componentManager, _, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);
        Assert.IsFalse(sourcePool.Has(SourceEntityId));

        // A second, different observer walking onto the same tile afterward proves the grid's
        // own contribution was actually retracted -- not just that the first observer's
        // exposure happened to clear.
        const int secondObserverEntityId = 150;
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, secondObserverEntityId);

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
    public void SourceWithTwoEffectTypes_MovingOntoAlreadyExposedObserver_GrantsBothTypes()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();

        var observerPosition = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        AddSource(componentManager, entityId: 150, observerPosition, StatusEffectType.Poison, strength: 1);
        // Pinned to Local so the tick loop below reliably reaches it -- an entity with no
        // ProcessingTierComponent yet fails open to Beyond, the slowest cadence (see
        // ProcessingTierWiring's own doc comment), which a single TickIntervalFrames loop
        // wouldn't otherwise catch.
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(ObserverEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        sourcePool.Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange));
        sourcePool.Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Poison, auraAndGlowStrength: 8, Color.DarkGreen));
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));

        // Establishes the observer's exposure (via the weak anchor Poison source) -- EnsureGrid's
        // bulk scatter, triggered by this same call, also picks up the dual-typed source's own
        // two instances, since both were placed before this very first Update (exactly how
        // PlaceTerrainOnMap would for real) -- this doesn't exercise the reactive Added path.
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);
        // Tick's own periodic pass requires a real Transform to still consider the entity
        // present (see Tick's own doc comment: no Transform reads as "gone," removing the
        // exposure entirely) -- MoveObserverTo only records the move event, it doesn't also
        // write the mover's own Transform.
        componentManager.Merge(ObserverEntityId, new TransformComponent(observerPosition, UnitSize));
        // Registered as a real map occupant -- GrantToOccupantsNear's box scan (triggered by the
        // SOURCE's own move below) needs to actually find it, the same way every other
        // "stationary occupant gets granted immediately" test already registers its own occupant.
        mapQuery.SetOccupant(observerPosition, ObserverEntityId);
        Assert.AreEqual(1, PoisonStackCountOf(componentManager, ObserverEntityId));

        componentManager.Merge(SourceEntityId, new TransformComponent(observerPosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, observerPosition, SourceEntityId);

        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Burning contribution from the moved dual-typed source must register in the grid.");
        Assert.AreEqual(9, PoisonStackCountOf(componentManager, ObserverEntityId), "Poison from both sources (1 + 8) is additive -- the moved source's own Poison instance is correctly chain-walked too, not just its Burning one.");
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Poison));
    }

    /// <summary>TotalStacksExcludingSelf must sum ALL of the entity's own matching-type sources, not just the first one a single-source check would have found.</summary>
    [TestMethod]
    public void SelfExclusion_EntityWithTwoSameTypeSources_ExcludesBothFromOwnReading()
    {
        var (system, componentManager, mapQuery, movedEntities, _) = Build();
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        sourcePool.Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange));
        sourcePool.Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 4, Color.Orange));
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, SourceEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, SourceEntityId), "Both of the source's own instances must be excluded from its own reading, not just the first one found.");
        Assert.IsFalse(HasExposure(componentManager, SourceEntityId, StatusEffectType.Burning));
    }

    /// <summary>The reported bug: a stationary target already standing where an aura is toggled on must be granted immediately, not have to wait until it happens to move.</summary>
    [TestMethod]
    public void SourceAddedViaToggle_StationaryOccupantAlreadyInRange_GrantsImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built" by the time the toggle below happens, same setup as the sync-bug regression tests above.
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();

        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "A stationary target already in range must be granted the moment the aura toggles on.");
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    /// <summary>
    /// Regression test for the fix: GrantToOccupantsNear/ReEvaluateExposuresNear used to scan
    /// IMapQuery.GetEntityIdsInBox, which only ever reports the Blocking occupant of a cell --
    /// a Tiny/Phasing (non-Blocking) occupant sharing that cell was silently invisible to
    /// auras. ObserverEntityId here is registered ONLY via SetNonBlockingOccupant (FakeMapQuery's
    /// non-Blocking index), never via SetOccupant/GetEntityIdAt, so this fails against the old
    /// GetEntityIdsInBox-based scan and passes only once the scan goes through
    /// GetOccupantEntityIdsAt instead.
    /// </summary>
    [TestMethod]
    public void SourceAddedViaToggle_StationaryNonBlockingOccupantAlreadyInRange_GrantsImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();

        // Forces EnsureGrid to run once with no sources present -- the grid is "already built" by the time the toggle below happens, same setup as the sync-bug regression tests above.
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();

        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetNonBlockingOccupant(SourcePosition, ObserverEntityId);

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "A stationary non-Blocking (e.g. Tiny/Phasing) occupant already in range must be granted too, not just Blocking ones.");
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }

    /// <summary>Mirrors the Added case above: toggling off must immediately clear a stationary nearby occupant's exposure, not leave it lingering until that occupant's own next scheduled tick.</summary>
    [TestMethod]
    public void SourceRemovedViaToggle_StationaryOccupantInRange_ExposureClearedImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        // ReEvaluateExposuresNear needs a real Transform to find the occupant at all (MoveObserverTo only records the move event, it doesn't also write the mover's own Transform).
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));

        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        Assert.IsFalse(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning), "Toggling off must immediately re-check nearby exposures, not wait for the observer's own next tick.");
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
    public void SourceCarryingActiveAura_MovesOntoStationaryOccupant_GrantsImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        // Toggle the aura on far away from the eventual target, forcing EnsureGrid to run first.
        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        // A stationary occupant standing where the source is about to walk to -- never itself moves.
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "A stationary occupant the source walks up to must be granted immediately, not wait for it to move itself.");
    }

    /// <summary>
    /// Part 2's own behavior change: a moving source that is NOT Local-tiered (Neighborhood
    /// here) must not resync its grid contribution synchronously on the spot -- see
    /// StatusEffectAuraSystem.ResyncSourceIfStale's own doc comment on why (an O(radius^2)
    /// resync per move is fine for a rare Local case, not for a whole population of far-away
    /// moving auras). Same setup as SourceCarryingActiveAura_MovesOntoStationaryOccupant_
    /// GrantsImmediately, but Neighborhood-tiered instead of Local, and expects the OPPOSITE
    /// outcome from a single move/Update call.
    /// </summary>
    [TestMethod]
    public void SourceAtNonLocalTier_MovingOntoStationaryOccupant_DoesNotGrantSynchronously()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "A non-Local source's grid resync must not happen synchronously on the move itself.");
    }

    /// <summary>Complements the test above: the deferred resync isn't lost, just delayed -- Update's own periodic catch-up pass (driven by the source's own tiered cadence) must eventually reach it and grant the stationary occupant.</summary>
    [TestMethod]
    public void SourceAtNonLocalTier_EventuallyResyncsViaPeriodicCatchUp()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        componentManager.GetDirectPool<ProcessingTierComponent>().Add(SourceEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        var farAwayStart = new Vector3Int(SourcePosition.X - 50, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(SourceEntityId, new TransformComponent(farAwayStart, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);
        movedEntities.ClearFrame();
        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);

        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        MoveObserverTo(system, movedEntities, farAwayStart, SourcePosition, SourceEntityId);
        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId), "Sanity check: still not resynced immediately after the move itself.");

        for (var frame = 0; frame < GenerousCatchUpFrameCount(system); frame++)
        {
            system.Update(new EngineTime(default, default, false, FrameCount: frame), (byte)(frame % system.StripeCount));
            movedEntities.ClearFrame();
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "The periodic catch-up pass must eventually resync a non-Local source's stale grid contribution and grant the stationary occupant.");
    }

    /// <summary>
    /// The bug this guards against: GrantToOccupantsNear/OnEntityMoved's fresh-entry check used
    /// to gate the grant CALL itself on !_exposures.Has(occupantId) -- a single flag covering
    /// "in range of *something*", not per effect type (see StatusEffectAuraExposureComponent's
    /// own doc comment) -- so an occupant already exposed to one aura (Burning here) never got a
    /// newly-toggled-on SECOND type (Poison) granted until its existing exposure's own tick
    /// happened to fire. A target already in range of a second aura must be granted that type
    /// immediately too, the same "immediately" guarantee
    /// SourceAddedViaToggle_StationaryOccupantAlreadyInRange_GrantsImmediately already covers
    /// for the zero-prior-exposure case.
    /// </summary>
    [TestMethod]
    public void SourceAddedViaToggle_OccupantAlreadyExposedToADifferentEffectType_GrantsNewTypeImmediately()
    {
        var (system, componentManager, mapQuery, movedEntities, eventBus) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        mapQuery.SetOccupant(SourcePosition, ObserverEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));

        const int secondSourceEntityId = 150;
        componentManager.Merge(secondSourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        AuraSourceEffects.Toggle(sourcePool, eventBus, secondSourceEntityId, StatusEffectType.Poison, auraAndGlowStrength: 8, Color.DarkGreen);

        Assert.AreEqual(8, PoisonStackCountOf(componentManager, ObserverEntityId), "Already being exposed to Burning must not block an immediate grant of the newly-toggled Poison source.");
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
    public void SpawnPositionEqualsOldPosition_StillGrantsFalloffStacks()
    {
        var (system, componentManager, _, movedEntities, _) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        movedEntities.Record(new EntityMovedEvent(ObserverEntityId, SourcePosition, SourcePosition, UnitSize));
        system.Update(new EngineTime(default, default, false, FrameCount: DrainOnlyFrameCount), 0);

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.IsTrue(HasExposure(componentManager, ObserverEntityId, StatusEffectType.Burning));
    }
}
