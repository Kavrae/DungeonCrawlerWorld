using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
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

    /// <summary>Minimal IMapQuery test double backing GetEntityIdsInBox with a real per-cell dictionary -- only used by StatusEffectAuraSystem's rare "a source moved" re-check path now that per-mover detection is an O(1) AuraGrid lookup, not a live scan.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int X, int Y, int Z), int> _occupantByPosition = [];

        public Vector3Int MapSize { get; } = new(1000, 1000, 3);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;

        public void SetOccupant(Vector3Int position, int entityId) => _occupantByPosition[(position.X, position.Y, position.Z)] = entityId;
        public void ClearOccupant(Vector3Int position) => _occupantByPosition.Remove((position.X, position.Y, position.Z));

        public int GetEntityIdAt(Vector3Int position) => _occupantByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;

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
    /// A stripe that never matches ObserverEntityId (0), SourceEntityId (100), or
    /// secondSourceEntityId (101) under StripeCount 15 (0, 10, and 11 respectively) -- used to
    /// drain a just-recorded move (grant/detect it) without also consuming one of the mover's
    /// own real CountdownTicker.Tick opportunities that same call, so existing rotating-stripe
    /// loops elsewhere in these tests keep landing on the same tick counts they always did.
    /// </summary>
    private const byte DrainOnlyStripeIndex = 1;

    /// <summary>Mirrors real game wiring (both BurningModule.Configure and PoisonModule.Configure registering their own applier into the same shared registry) -- the registry a caller can override via applierRegistry to exercise unsupported-effect-type behavior instead.</summary>
    private static (StatusEffectAuraSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, FrameEventBuffer<EntityMoved> MovedEntities) Build(StatusEffectAuraApplierRegistry? applierRegistry = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 200, initialComponentCapacity: 50);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<StatusEffectAuraSourceComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<StatusEffectAuraExposureComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var movedEntities = new FrameEventBuffer<EntityMoved>();

        var system = new StatusEffectAuraSystem(
            componentManager,
            componentManager.GetPackedPool<StatusEffectAuraExposureComponent>(),
            componentManager.GetPackedPool<StatusEffectAuraSourceComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            mapQuery,
            applierRegistry ?? DefaultApplierRegistry(),
            movedEntities,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, movedEntities);
    }

    private static StatusEffectAuraApplierRegistry DefaultApplierRegistry()
    {
        var registry = new StatusEffectAuraApplierRegistry();
        registry.Register(new TimerBasedAuraApplier<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.ApplyStack));
        registry.Register(new TimerBasedAuraApplier<PoisonTimerComponent>(StatusEffectType.Poison, (cm, id, source) => PoisonEffects.ApplyStack(cm, id, source, durationInTicks: 1)));
        return registry;
    }

    /// <summary>AuraGrid only finds a source by scanning its TransformComponent (see StatusEffectAuraSystem.EnsureGrid) -- a source needs one set at its real position for any of these tests to see it, exactly as PlaceTerrainOnMap/PlaceEntityOnMap would set it for real in-game.</summary>
    private static void AddSource(ComponentManager componentManager, int entityId, Vector3Int position, StatusEffectType effectType, int strength)
    {
        componentManager.GetPackedPool<StatusEffectAuraSourceComponent>().Add(entityId, new StatusEffectAuraSourceComponent(effectType, strength, Color.Orange));
        componentManager.Merge(entityId, new TransformComponent(position, UnitSize));
    }

    /// <summary>
    /// Records the move into the shared buffer and immediately drains it via a stripe that
    /// can't touch the mover's own exposure timer -- see DrainOnlyStripeIndex's own doc comment
    /// for why this doesn't consume a real tick opportunity. Also clears the buffer afterward,
    /// the same way SystemManager would at the end of a real frame's cycle (see FrameEventBuffer's
    /// own doc comment) -- these tests construct StatusEffectAuraSystem directly, bypassing
    /// SystemManager entirely, so without this the recorded move would still be sitting in the
    /// buffer on every later Update call in these tests' own tick loops, getting silently
    /// reprocessed (re-detecting the same move, over and over) instead of just once.
    /// </summary>
    private static void MoveObserverTo(StatusEffectAuraSystem system, FrameEventBuffer<EntityMoved> movedEntities, Vector3Int from, Vector3Int to, int entityId = ObserverEntityId)
    {
        movedEntities.Record(new EntityMoved(entityId, from, to, UnitSize));
        system.Update(default, DrainOnlyStripeIndex);
        movedEntities.ClearFrame();
    }

    private static int StackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<BurningTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

    private static int PoisonStackCountOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<PoisonTimerComponent>().TryGetReadonly(entityId, out var timer) ? timer.StackCount : 0;

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
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    [TestMethod]
    public void TwoOverlappingSources_StacksAreAdditive()
    {
        var (system, componentManager, _, movedEntities) = Build();
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
        var (system, componentManager, _, movedEntities) = Build();
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
            system.Update(default, (byte)(frame % system.StripeCount));
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
    }

    /// <summary>Complements the test above: if something else (BurningSystem's own decay, in the real game) reduced the stack count below the target in between, the aura tops it back up to the target rather than ignoring the shortfall or overshooting past it.</summary>
    [TestMethod]
    public void RemainingInRange_ToppsBackUpToTargetAfterExternalDecay()
    {
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        // Simulate BurningSystem's own decay (not exercised by this system-level test) having
        // brought the stack count down since the last aura tick.
        componentManager.GetPackedPool<BurningTimerComponent>().TryUpdate(ObserverEntityId, static (ref BurningTimerComponent t) => t.StackCount = 5);

        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(default, (byte)(frame % system.StripeCount));
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
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.IsTrue(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);

        Assert.IsTrue(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    [TestMethod]
    public void ObserverWalksOutOfRange_ExposureRemovedOnceTimerTicksWhileStillAway()
    {
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition);
        // Update reads the observer's *current* Transform.Position, independent of the
        // EntityMoved event itself -- must reflect where it actually ended up.
        componentManager.Merge(ObserverEntityId, new TransformComponent(farAwayPosition, UnitSize));

        for (var frame = 0; frame < AuraEffects.TickIntervalFrames; frame++)
        {
            system.Update(default, (byte)(frame % system.StripeCount));
        }

        Assert.IsFalse(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    /// <summary>
    /// The bug this was written to catch: stepping out of an aura and back in before the
    /// timer's next scheduled tick must grant nothing extra and must not restart the
    /// countdown -- unlike ContactDamageSystem, which deliberately re-triggers on every step.
    /// </summary>
    [TestMethod]
    public void MovingOutAndBackInBeforeNextTick_DoesNotRegrantOrResetTimer()
    {
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));

        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(default, (byte)(frame % system.StripeCount));
        }

        Assert.AreEqual(30, componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().GetReadonly(ObserverEntityId).FramesUntilNextTick);

        // Step out (still in range at distance 1 -- but exposure already exists, so this
        // must not grant) and back in, all before the original timer would naturally tick.
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
        MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        componentManager.Merge(ObserverEntityId, new TransformComponent(SourcePosition, UnitSize));

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "Stepping out and back in before the timer ticks must not grant again.");
        Assert.AreEqual(30, componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().GetReadonly(ObserverEntityId).FramesUntilNextTick, "...nor reset the timer.");

        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(default, (byte)(frame % system.StripeCount));
        }

        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId), "The original timer reaching 0 re-evaluates based on the entity's current (in-range) position, topping off to the target rather than adding to it again.");
    }

    [TestMethod]
    public void RepeatedEntryExitCycles_NeverThrowsAndNeverDuplicatesExposure()
    {
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        var oneTileAway = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);
        for (var i = 0; i < 10; i++)
        {
            MoveObserverTo(system, movedEntities, SourcePosition, oneTileAway);
            MoveObserverTo(system, movedEntities, oneTileAway, SourcePosition);
        }

        // PackedComponentPool.Add throws on a duplicate entity id, so simply not throwing
        // above already proves at most one exposure component ever existed; this just also
        // confirms it never re-granted along the way.
        Assert.AreEqual(8, StackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(1, componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Count);
    }

    /// <summary>
    /// The scenario Question 4 (from the original design review) asked about directly: if the
    /// source (not the observer) moves away, a stationary observer's exposure must still be
    /// cleared, not left stuck forever.
    /// </summary>
    [TestMethod]
    public void SourceMovesAwayFromStationaryObserver_ExposureRemoved()
    {
        var (system, componentManager, mapQuery, movedEntities) = Build();
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
        Assert.IsTrue(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));

        // The source itself moves far away -- the observer never moves again. Transform is
        // updated to the new position first, mirroring EntityMoved's own contract ("Position
        // is already updated by the time this fires").
        mapQuery.ClearOccupant(SourcePosition);
        var farAwayPosition = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        mapQuery.SetOccupant(farAwayPosition, SourceEntityId);
        componentManager.Merge(SourceEntityId, new TransformComponent(farAwayPosition, UnitSize));
        MoveObserverTo(system, movedEntities, SourcePosition, farAwayPosition, SourceEntityId);

        Assert.IsFalse(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    [TestMethod]
    public void SourceDoesNotIgniteItself()
    {
        var (system, componentManager, mapQuery, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        mapQuery.SetOccupant(SourcePosition, SourceEntityId);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition, SourceEntityId);

        Assert.AreEqual(0, StackCountOf(componentManager, SourceEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(SourceEntityId));
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
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Poison, strength: 8);

        var observerPosition = new Vector3Int(SourcePosition.X + distance, SourcePosition.Y, SourcePosition.Z);
        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), observerPosition);

        Assert.AreEqual(expectedStacks, PoisonStackCountOf(componentManager, ObserverEntityId));
        Assert.AreEqual(expectedStacks > 0, componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    /// <summary>A corpse doesn't accumulate new stacks from a nearby aura source -- see DeathSystem/DeadComponent.</summary>
    [TestMethod]
    public void DeadObserver_InRange_GrantsNothing()
    {
        var (system, componentManager, _, movedEntities) = Build();
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Burning, strength: 8);
        componentManager.GetPackedPool<DeadComponent>().Add(ObserverEntityId, new DeadComponent(KilledByEntityId: null));

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, StackCountOf(componentManager, ObserverEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId));
    }

    /// <summary>StatusEffectAuraSourceComponent generalizes over EffectType, but granting still requires some module to have actually registered an IStatusEffectAuraApplier for it (see GrantStacks) -- a source authored for an effect type nothing has registered yet must grant nothing rather than throwing, and since nothing is actually granted, it must not track exposure either. GrantStacks reports "has a registered applier or not" precisely so TryGrantApplicableStacks doesn't infer "in range of something real" from a grid whose effect type nothing can apply.</summary>
    [TestMethod]
    public void EffectTypeWithNoRegisteredApplier_GrantsNothingAndTracksNoExposure()
    {
        var (system, componentManager, _, movedEntities) = Build(new StatusEffectAuraApplierRegistry());
        AddSource(componentManager, SourceEntityId, SourcePosition, StatusEffectType.Poison, strength: 8);

        MoveObserverTo(system, movedEntities, new Vector3Int(0, 0, 0), SourcePosition);

        Assert.AreEqual(0, PoisonStackCountOf(componentManager, ObserverEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<StatusEffectAuraExposureComponent>().Has(ObserverEntityId),
            "An effect type with no registered applier grants nothing, so it must not create a phantom exposure either.");
    }
}
