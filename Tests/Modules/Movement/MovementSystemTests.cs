using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests.Modules.Movement;

/// <summary>
/// MovementSystem is purely reactive now -- it executes whatever NextMapPosition is already set,
/// it never decides one (see MovementSystem's own doc comment). Random-mode wander-decision
/// coverage that used to live here (idle coin-flip, direction search, non-Blocking bypass) moved
/// to MovementCandidatesTests, since that math now lives in MovementCandidates and is exercised
/// by TestCombatBehaviorSystem instead of this system. Tests below that used to rely on
/// MovementSystem's own Random-mode auto-decision to produce a move now pre-set
/// MovementComponent.NextMapPosition directly, the same way a real caller (Presentation input,
/// or TestCombatBehaviorSystem) would have queued it upstream this same frame.
/// </summary>
[TestClass]
public sealed class MovementSystemTests
{
    /// <summary>
    /// A minimal IMapQuery test double with no Game.World.World involved at all -- proves
    /// MovementSystem's dependency on World was actually removed, not just hidden behind an
    /// interface that only World happens to implement.
    /// </summary>
    private sealed class FakeMapQuery(Vector3Int mapSize) : IMapQuery
    {
        public Vector3Int MapSize { get; } = mapSize;
        public bool IsOnMap(Vector3Int position) =>
            position.X >= 0 && position.Y >= 0 && position.Z >= 0
            && position.X < MapSize.X && position.Y < MapSize.Y && position.Z < MapSize.Z;
        public int GetEntityIdAt(Vector3Int position) => -1;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);
    }

    /// <summary>Records the last SyncMove call instead of touching any real World -- pairs with FakeMapQuery so a test can run with no Game.World.World anywhere in the object graph while still verifying the mandatory map-sync path was invoked.</summary>
    private sealed class RecordingEntityMoveSync : IEntityMoveSync
    {
        public EntityMovedEvent? LastSynced { get; private set; }
        public void SyncMove(EntityMovedEvent moved) => LastSynced = moved;
        public void ConvertToNonBlocking(int entityId, ref TransformComponent transform) { }
    }

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static DirectComponentPool<TransformComponent> CreateTransformPool(int capacity = 10) =>
        new(capacity, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<ActionLockComponent> CreateActionLockPool(int capacity = 10) =>
        new(capacity, capacity, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<MovementComponent> CreateMovementPool(int capacity = 10) =>
        new(capacity, capacity, static (ref existing, incoming) => existing = incoming);

    [TestMethod]
    public void Update_MissingActionLockOrTransformComponent_IsSkippedWithoutThrowing()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));
        // Entity 0 has no TransformComponent or ActionLockComponent registered.

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());

        system.Update(default, 0);
    }

    /// <summary>
    /// MovementSystem only ever reads the shared action lock -- decrementing it is
    /// ActionLockSystem's job (see ActionLockComponent's own doc comment for why), so
    /// LockFramesRemaining must be unchanged, not decremented, after MovementSystem.Update.
    /// </summary>
    [TestMethod]
    public void Update_ActionLocked_DoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 3, lockFramesRemaining: 3));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(3, actionLockPool.GetReadonly(0).LockFramesRemaining);
        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    [TestMethod]
    public void Update_DeadEntity_DoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents(), deadEntities);
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    /// <summary>
    /// Regression test for the CanMove fix (decision #8): Old's multi-tile collision check
    /// only inspected cells that were OFF the map (an inverted condition), so an on-map
    /// cell already occupied by another entity was never actually treated as blocking.
    /// This sets up a 2x1 entity with its queued target's footprint partially occupied by
    /// another entity, and asserts it doesn't move -- MovementCandidates.CanOccupy (via
    /// TryMoveToNextMapPosition's re-validation) must still catch the on-map-blocked cell.
    /// </summary>
    [TestMethod]
    public void Update_MultiTileEntityTargetPartiallyOccupied_DoesNotMoveIntoOccupiedCell()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        // Mover: 2x1 footprint at (0,0,0), queued to step south to (0,1,0)+(1,1,0) -- one cell
        // of which is already occupied by another entity.
        var moverTransform = new TransformComponent(new Vector3Int(0, 0, 0), new Vector2Byte(2, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(0, 1, 0)));

        var blockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, blockerTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(1, 1, 0), ref blockerTransform);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(0, 0, 0), transformPool.GetReadonly(0).Position);
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition, "A rejected target must be cleared so a fresh one can be queued.");
        Assert.AreEqual(0, actionLockPool.GetReadonly(0).LockFramesRemaining, "A rejected move isn't an action -- it must not touch the shared action lock.");
    }

    /// <summary>
    /// Regression test: another entity claiming NextMapPosition's target between selection and
    /// execution (e.g. the player queues a move the same real frame a wandering NPC steps into
    /// that cell first) used to go uncaught -- TryMoveToNextMapPosition wrote
    /// TransformComponent.Position unconditionally, World.MoveEntity then silently no-opped on
    /// the collision (its own defensive check), leaving TransformComponent.Position pointing
    /// at a cell the Map's occupancy array never actually granted the mover -- desynced state
    /// that made the mover's glyph stop drawing anywhere (MapWindow.DrawPrimaryOccupant looks
    /// up the occupant per Map cell, not per entity). CanOccupy must be re-checked here too, not
    /// just at selection time, and a blocked target must not corrupt Position or Map occupancy.
    /// </summary>
    [TestMethod]
    public void Update_TargetClaimedByAnotherEntityBeforeExecution_DoesNotMoveOrCorruptOccupancy()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var startPosition = new Vector3Int(2, 2, 0);
        var contestedPosition = new Vector3Int(3, 2, 0);

        var moverTransform = new TransformComponent(startPosition, new Vector2Byte(1, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.PlayerControlled, 10, null, contestedPosition));

        // Another entity already occupies the mover's queued target, simulating it having
        // moved there since the mover's NextMapPosition was selected.
        var blockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, blockerTransform);
        world.PlaceEntityOnMap(1, contestedPosition, ref blockerTransform);

        EntityMovedEvent? received = null;
        var eventBus = new EventBus();
        eventBus.Subscribe<EntityMovedEvent>(e => received = e);
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, eventBus, new WorldEventSync(world), movedEntities, new FakePlayerQuery(0), CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(startPosition, transformPool.GetReadonly(0).Position, "Mover must stay put -- the target was already taken.");
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition, "The stale target must be cleared so a fresh one can be queued.");
        Assert.IsNull(received, "No move actually happened, so no EntityMovedEvent should publish.");
        Assert.IsEmpty(movedEntities.Items, "No move actually happened, so nothing should be recorded either.");
        Assert.AreEqual(0, world.GetEntityIdAt(startPosition), "The mover's own cell must still correctly list the mover.");
        Assert.AreEqual(1, world.GetEntityIdAt(contestedPosition), "The contested cell must still correctly list only the blocker.");
    }

    [TestMethod]
    public void Update_StuckSearchCooldownPositive_DecrementsByStripeCountAndDoesNotSearchForNewPosition()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null) { FramesToWait = 40 });

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(25, movementPool.GetReadonly(0).FramesToWait);
        Assert.AreEqual(0, actionLockPool.GetReadonly(0).LockFramesRemaining);
        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    [TestMethod]
    public void Update_StuckSearchCooldownBelowStripeCount_ClampsToZeroInsteadOfGoingNegative()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null) { FramesToWait = 6 });

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(0, movementPool.GetReadonly(0).FramesToWait);
    }

    /// <summary>
    /// Confirms MovementSystem runs against a bare IMapQuery fake with no World anywhere in
    /// the object graph, and that a confirmed move reaches its consumers via IEntityMoveSync
    /// (mandatory map sync) and the shared FrameEventBuffer (optional/bulk consumers) rather
    /// than calling into World directly -- the two halves of decision #2's read/write split,
    /// updated for the buffer-based redesign that replaced the old single EventBus.Publish
    /// (see MovementSystem's own doc comment for why).
    /// </summary>
    [TestMethod]
    public void Update_SuccessfulMove_SyncsMoveAndRecordsItWithoutTouchingWorld()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var entityMoveSync = new RecordingEntityMoveSync();
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        var startPosition = new Vector3Int(2, 2, 0);
        var targetPosition = new Vector3Int(3, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, targetPosition));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new EventBus(), entityMoveSync, movedEntities, null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.IsNotNull(entityMoveSync.LastSynced);
        Assert.AreEqual(0, entityMoveSync.LastSynced!.Value.EntityId);
        Assert.AreEqual(startPosition, entityMoveSync.LastSynced.Value.OldPosition);
        Assert.AreEqual(targetPosition, entityMoveSync.LastSynced.Value.NewPosition);
        Assert.AreEqual(targetPosition, transformPool.GetReadonly(0).Position);

        Assert.HasCount(1, movedEntities.Items);
        Assert.AreEqual(entityMoveSync.LastSynced.Value, movedEntities.Items[0]);
    }

    /// <summary>Regression test for the redesign's dual dispatch: EventBus.Publish&lt;EntityMovedEvent&gt; is now reserved for the player's own move (a handful/sec) instead of firing for the whole population, since PlayerActivityLog subscribes to it directly and expects nothing else on the bus.</summary>
    [TestMethod]
    public void Update_PlayerControlledMoversMove_AlsoPublishesEntityMovedForThatEntityOnly()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var eventBus = new EventBus();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));

        EntityMovedEvent? received = null;
        eventBus.Subscribe<EntityMovedEvent>(e => received = e);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, eventBus, new RecordingEntityMoveSync(), new FrameEventBuffer<EntityMovedEvent>(), new FakePlayerQuery(0), CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.IsNotNull(received, "The mover IS the configured player, so its move must still publish via EventBus.");
        Assert.AreEqual(0, received!.Value.EntityId);
    }

    /// <summary>Complements the test above: a non-player mover's move must NOT publish via EventBus, even though it's still synced/recorded via the other two channels -- otherwise every wandering NPC would still pay EventBus dispatch cost, the exact hotspot this redesign removed.</summary>
    [TestMethod]
    public void Update_NonPlayerMoverMoves_DoesNotPublishEntityMovedViaEventBus()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var eventBus = new EventBus();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));

        var published = false;
        eventBus.Subscribe<EntityMovedEvent>(_ => published = true);
        var movedEntities = new FrameEventBuffer<EntityMovedEvent>();

        // playerEntityId (99) never matches the mover (0) -- also covers a null IPlayerQuery, matching most tests above.
        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, eventBus, new RecordingEntityMoveSync(), movedEntities, new FakePlayerQuery(99), CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.IsFalse(published);
        Assert.HasCount(1, movedEntities.Items, "The move must still reach the buffer-based consumers even though it's not the player's.");
    }

    /// <summary>
    /// Mirrors MapWindow.TryQueuePlayerMove's validate-then-queue pattern (on-map + free-space
    /// check before ever setting NextMapPosition -- MovementSystem itself never decides a
    /// destination for any mode) applied to two independent entities, proving
    /// MovementMode.PlayerControlled isn't tied to any single global "the player":
    /// World.PlayerEntityId is just which PlayerControlled entity MapWindow's input happens to
    /// drive today, not a constraint MovementSystem enforces. Entity ids 0 and 15 share stripe
    /// bucket 0 (StripeCount = 15), so a single Update(_, 0) call processes both in the same
    /// tick.
    /// </summary>
    [TestMethod]
    public void Update_TwoPlayerControlledEntities_EachMoveIndependentlyWithOwnValidation()
    {
        var transformPool = CreateTransformPool(20);
        var actionLockPool = CreateActionLockPool(20);
        var movementPool = CreateMovementPool(20);
        var world = new Game.World.World(new Map(new Vector3Int(10, 10, 1)));

        const int firstEntityId = 0;
        const int secondEntityId = 15;
        const int wallEntityId = 3;

        var firstTransform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(firstEntityId, firstTransform);
        world.PlaceEntityOnMap(firstEntityId, firstTransform.Position, ref firstTransform);
        actionLockPool.Add(firstEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(firstEntityId, new MovementComponent(MovementMode.PlayerControlled, 10, null, null));

        var secondTransform = new TransformComponent(new Vector3Int(7, 7, 0), new Vector2Byte(1, 1));
        transformPool.Add(secondEntityId, secondTransform);
        world.PlaceEntityOnMap(secondEntityId, secondTransform.Position, ref secondTransform);
        actionLockPool.Add(secondEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(secondEntityId, new MovementComponent(MovementMode.PlayerControlled, 10, null, null));

        // A wall directly east of the second entity -- its own move attempt there must be
        // rejected by its own validation, independent of the first entity's unrelated move.
        var wallTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(wallEntityId, wallTransform);
        world.PlaceEntityOnMap(wallEntityId, new Vector3Int(8, 7, 0), ref wallTransform);

        QueuePlayerControlledMove(world, transformPool, movementPool, firstEntityId, new Vector3Int(1, 0, 0));
        QueuePlayerControlledMove(world, transformPool, movementPool, secondEntityId, new Vector3Int(1, 0, 0)); // Into the wall.

        Assert.AreEqual(new Vector3Int(3, 2, 0), movementPool.GetReadonly(firstEntityId).NextMapPosition);
        Assert.IsNull(movementPool.GetReadonly(secondEntityId).NextMapPosition,
            "The second entity's own validation must reject a move into the wall, independent of the first entity's move.");

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(3, 2, 0), transformPool.GetReadonly(firstEntityId).Position, "First entity moves to its own valid target.");
        Assert.AreEqual(new Vector3Int(7, 7, 0), transformPool.GetReadonly(secondEntityId).Position, "Second entity stays put -- it never had a valid queued move.");
    }

    /// <summary>A diagonal step covers √2 the distance of a cardinal one, so the shared ActionLock it sets must scale by the same factor.</summary>
    [TestMethod]
    public void Update_DiagonalMove_ScalesActionLockBySqrtTwo()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.PlayerControlled, 20, null, new Vector3Int(3, 3, 0)));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(3, 3, 0), transformPool.GetReadonly(0).Position);
        Assert.AreEqual(28, actionLockPool.GetReadonly(0).TotalLockFrames, "round(20 * 1.41421356) == 28.");
    }

    /// <summary>Regression coverage for corner-cutting prevention: a diagonal move must be rejected when both flanking orthogonal tiles are blocked, since neither side of the corner is actually open to pass through.</summary>
    [TestMethod]
    public void Update_DiagonalMove_BothFlanksBlocked_DoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var startPosition = new Vector3Int(2, 2, 0);
        var transform = new TransformComponent(startPosition, new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.PlayerControlled, 20, null, new Vector3Int(3, 3, 0)));

        // Both tiles flanking the diagonal step from (2,2,0) to (3,3,0) -- (3,2,0) and (2,3,0) -- are walls.
        var wallOneTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, wallOneTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(3, 2, 0), ref wallOneTransform);

        var wallTwoTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(2, wallTwoTransform);
        world.PlaceEntityOnMap(2, new Vector3Int(2, 3, 0), ref wallTwoTransform);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(startPosition, transformPool.GetReadonly(0).Position, "Both corner flanks blocked -- the diagonal cut must be rejected.");
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition);
        Assert.AreEqual(0, actionLockPool.GetReadonly(0).LockFramesRemaining, "A rejected move isn't an action -- it must not touch the shared action lock.");
    }

    /// <summary>Complements the test above: only one flanking tile blocked still leaves a way through the corner, so the diagonal move must succeed.</summary>
    [TestMethod]
    public void Update_DiagonalMove_OneFlankBlocked_StillMoves()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        var transform = new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, transform);
        world.PlaceEntityOnMap(0, transform.Position, ref transform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.PlayerControlled, 20, null, new Vector3Int(3, 3, 0)));

        // Only one of the two flanking tiles, (3,2,0), is a wall -- (2,3,0) stays open.
        var wallTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, wallTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(3, 2, 0), ref wallTransform);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMovedEvent>(), null, CreateProcessingTierPool(), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(3, 3, 0), transformPool.GetReadonly(0).Position, "One flank still open -- the diagonal move must succeed.");
    }

    private static DirectComponentPool<ProcessingTierComponent> CreateProcessingTierPool(int capacity = 10) =>
        new(capacity, static (ref existing, incoming) => existing = incoming);

    /// <summary>Entity 0's (entityId + FrameCount) % CycleDivisor is 1 % 2 != 0 -- an off cycle for a Neighborhood-tiered entity, so it must be skipped even with a valid target already queued.</summary>
    [TestMethod]
    public void Update_ThrottledEntity_OffCycle_DoesNotMove()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var processingTiers = CreateProcessingTierPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var entityMoveSync = new RecordingEntityMoveSync();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));
        processingTiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        // Entity 0, Neighborhood-tiered (StripeCount 15 * divisor 2 = 30), lands in bucket 0 -- due only when FrameCount % 30 == 0.
        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new EventBus(), entityMoveSync, new FrameEventBuffer<EntityMovedEvent>(), null, processingTiers, new ProcessingTierEvents());
        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.IsNull(entityMoveSync.LastSynced);
        Assert.AreEqual(startPosition, transformPool.GetReadonly(0).Position);
    }

    /// <summary>Complements the test above: FrameCount chosen so (entityId + FrameCount) % CycleDivisor == 0 -- an eligible cycle, so the same Neighborhood-tiered entity moves normally.</summary>
    [TestMethod]
    public void Update_ThrottledEntity_OnEligibleCycle_Moves()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var processingTiers = CreateProcessingTierPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var entityMoveSync = new RecordingEntityMoveSync();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));
        processingTiers.Add(0, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new EventBus(), entityMoveSync, new FrameEventBuffer<EntityMovedEvent>(), null, processingTiers, new ProcessingTierEvents());
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.IsNotNull(entityMoveSync.LastSynced);
        Assert.AreNotEqual(startPosition, transformPool.GetReadonly(0).Position);
    }

    /// <summary>The pool is wired (unlike every test above, which passes null outright) but this entity has never been visited by ProcessingTierSystem, so it has no ProcessingTierComponent yet -- must fail open to full, unthrottled processing rather than being treated as maximally throttled.</summary>
    [TestMethod]
    public void Update_ProcessingTierPoolWiredButEntityUntiered_ProcessesNormally()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var processingTiers = CreateProcessingTierPool();
        var mapQuery = new FakeMapQuery(new Vector3Int(5, 5, 1));
        var entityMoveSync = new RecordingEntityMoveSync();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, new Vector3Int(3, 2, 0)));
        // Entity 0 deliberately has no ProcessingTierComponent -- defaults to Local tier (divisor 1), the same cadence as before ProcessingTier existed (StripeCount 15, bucket 0, due when FrameCount % 15 == 0).
        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new EventBus(), entityMoveSync, new FrameEventBuffer<EntityMovedEvent>(), null, processingTiers, new ProcessingTierEvents());
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        Assert.IsNotNull(entityMoveSync.LastSynced);
        Assert.AreNotEqual(startPosition, transformPool.GetReadonly(0).Position);
    }

    /// <summary>Standalone stand-in for MapWindow.TryQueuePlayerMove's validate-then-queue logic, so this test can drive independent PlayerControlled entities without any MapWindow/input machinery.</summary>
    private static void QueuePlayerControlledMove(IMapQuery mapQuery, DirectComponentPool<TransformComponent> transformPool, PackedComponentPool<MovementComponent> movementPool, int entityId, Vector3Int delta)
    {
        if (!transformPool.TryGetReadonly(entityId, out var transformComponent) || !movementPool.TryGetReadonly(entityId, out var movementComponent))
        {
            return;
        }

        var isAtRest = movementComponent.NextMapPosition is null || movementComponent.NextMapPosition.Value == transformComponent.Position;
        if (!isAtRest)
        {
            return;
        }

        var candidate = transformComponent.Position + delta;
        var occupyingEntityId = mapQuery.GetEntityIdAt(candidate);
        if (!mapQuery.IsOnMap(candidate) || (occupyingEntityId != -1 && occupyingEntityId != entityId))
        {
            return;
        }

        movementPool.TryUpdate(entityId, candidate, static (ref MovementComponent movement, Vector3Int target) => movement.NextMapPosition = target);
    }
}
