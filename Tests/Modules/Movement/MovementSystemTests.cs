using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.World;

namespace Tests.Modules.Movement;

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
        public EntityMoved? LastSynced { get; private set; }
        public void SyncMove(EntityMoved moved) => LastSynced = moved;
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

    private static MultiComponentPool<NonBlockingComponent> CreateNonBlockingPool(int capacity = 10) =>
        new(capacity, capacity);

    [TestMethod]
    public void Update_MissingActionLockOrTransformComponent_IsSkippedWithoutThrowing()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));
        // Entity 0 has no TransformComponent or ActionLockComponent registered.

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);

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
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
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
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));
        deadEntities.Add(0, new DeadComponent(KilledByEntityId: null));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null, deadEntities);
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(2, 2, 0), transformPool.GetReadonly(0).Position);
    }

    /// <summary>
    /// Regression test for the CanMove fix (decision #8): Old's multi-tile collision check
    /// only inspected cells that were OFF the map (an inverted condition), so an on-map
    /// cell already occupied by another entity was never actually treated as blocking.
    /// This sets up a 2x1 entity with all four neighboring positions invalid -- two via map
    /// edges, two via other entities occupying on-map cells in the target footprint -- and
    /// asserts it doesn't move. Under the old bug, the two on-map-blocked directions would
    /// have incorrectly been treated as free, and the entity would move into an occupied cell.
    /// </summary>
    [TestMethod]
    public void Update_MultiTileEntitySurroundedByOnMapObstacles_DoesNotMoveIntoOccupiedCell()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));

        // Mover: 2x1 footprint at (0,0,0). North and East are blocked by the map edge
        // (Position.X==0, Position.Y==0). South (target footprint (0,1,0)+(1,1,0)) and
        // West (target footprint (1,0,0)+(2,0,0)) are blocked by other entities occupying
        // one cell of each target footprint -- both clearly on-map.
        var moverTransform = new TransformComponent(new Vector3Int(0, 0, 0), new Vector2Byte(2, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        var southBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, southBlockerTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(1, 1, 0), ref southBlockerTransform);

        var westBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(2, westBlockerTransform);
        world.PlaceEntityOnMap(2, new Vector3Int(2, 0, 0), ref westBlockerTransform);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(new Random(1)), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(0, 0, 0), transformPool.GetReadonly(0).Position);
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition);
        // All four directions exhausted -- SetRandomMapPosition falls through to the
        // "no valid options" branch, which sets MovementComponent's own FramesToWait, not the
        // shared action lock (failing to find a spot isn't an action).
        Assert.AreEqual(120, movementPool.GetReadonly(0).FramesToWait);
        Assert.AreEqual(0, actionLockPool.GetReadonly(0).LockFramesRemaining);
    }

    /// <summary>
    /// Regression test: another entity claiming NextMapPosition's target between selection and
    /// execution (e.g. the player queues a move the same real frame a wandering NPC steps into
    /// that cell first) used to go uncaught -- TryMoveToNextMapPosition wrote
    /// TransformComponent.Position unconditionally, World.MoveEntity then silently no-opped on
    /// the collision (its own defensive check), leaving TransformComponent.Position pointing
    /// at a cell the Map's occupancy array never actually granted the mover -- desynced state
    /// that made the mover's glyph stop drawing anywhere (MapWindow.DrawPrimaryOccupant looks
    /// up the occupant per Map cell, not per entity). CanMove must be re-checked here too, not
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

        EntityMoved? received = null;
        var eventBus = new EventBus();
        eventBus.Subscribe<EntityMoved>(e => received = e);
        var movedEntities = new FrameEventBuffer<EntityMoved>();

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), eventBus, new WorldEventSync(world), movedEntities, new FakePlayerQuery(0));
        system.Update(default, 0);

        Assert.AreEqual(startPosition, transformPool.GetReadonly(0).Position, "Mover must stay put -- the target was already taken.");
        Assert.IsNull(movementPool.GetReadonly(0).NextMapPosition, "The stale target must be cleared so a fresh one can be queued.");
        Assert.IsNull(received, "No move actually happened, so no EntityMoved should publish.");
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

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
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

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
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
        var movedEntities = new FrameEventBuffer<EntityMoved>();

        var startPosition = new Vector3Int(2, 2, 0);
        transformPool.Add(0, new TransformComponent(startPosition, new Vector2Byte(1, 1)));
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new MathUtility(new Random(1)), new EventBus(), entityMoveSync, movedEntities, null);
        system.Update(default, 0);

        Assert.IsNotNull(entityMoveSync.LastSynced);
        Assert.AreEqual(0, entityMoveSync.LastSynced!.Value.EntityId);
        Assert.AreEqual(startPosition, entityMoveSync.LastSynced.Value.OldPosition);
        Assert.AreEqual(transformPool.GetReadonly(0).Position, entityMoveSync.LastSynced.Value.NewPosition);
        Assert.AreNotEqual(startPosition, entityMoveSync.LastSynced.Value.NewPosition);

        Assert.HasCount(1, movedEntities.Items);
        Assert.AreEqual(entityMoveSync.LastSynced.Value, movedEntities.Items[0]);
    }

    /// <summary>Regression test for the redesign's dual dispatch: EventBus.Publish&lt;EntityMoved&gt; is now reserved for the player's own move (a handful/sec) instead of firing for the whole population, since PlayerActivityLog subscribes to it directly and expects nothing else on the bus.</summary>
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
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        EntityMoved? received = null;
        eventBus.Subscribe<EntityMoved>(e => received = e);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new MathUtility(new Random(1)), eventBus, new RecordingEntityMoveSync(), new FrameEventBuffer<EntityMoved>(), new FakePlayerQuery(0));
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
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 10, null, null));

        var published = false;
        eventBus.Subscribe<EntityMoved>(_ => published = true);
        var movedEntities = new FrameEventBuffer<EntityMoved>();

        // playerEntityId (99) never matches the mover (0) -- also covers a null IPlayerQuery, matching most tests above.
        var system = new MovementSystem(transformPool, actionLockPool, movementPool, mapQuery, new MathUtility(new Random(1)), eventBus, new RecordingEntityMoveSync(), movedEntities, new FakePlayerQuery(99));
        system.Update(default, 0);

        Assert.IsFalse(published);
        Assert.HasCount(1, movedEntities.Items, "The move must still reach the buffer-based consumers even though it's not the player's.");
    }

    /// <summary>
    /// Mirrors Update_MultiTileEntitySurroundedByOnMapObstacles_DoesNotMoveIntoOccupiedCell's
    /// setup (corner position, two directions excluded by the map edge, the remaining two
    /// occupied by other Blocking entities) but for a non-Blocking mover -- where that test
    /// asserts the entity gets stuck (action lock set, all four directions exhausted), a
    /// non-Blocking mover must bypass the occupancy comparison entirely (see CanMove, which
    /// only ever asks IMapQuery.IsBlocking -- it doesn't know or care whether that's backed by
    /// NonBlockingComponent, ForceBlockingComponent, or anything else) and move regardless of
    /// the two blockers.
    /// </summary>
    [TestMethod]
    public void Update_NonBlockingMover_BypassesEntitiesBlockingEveryOtherDirection()
    {
        var transformPool = CreateTransformPool();
        var actionLockPool = CreateActionLockPool();
        var movementPool = CreateMovementPool();
        var nonBlockingPool = CreateNonBlockingPool();
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1))) { NonBlockingComponents = nonBlockingPool };

        var moverTransform = new TransformComponent(new Vector3Int(0, 0, 0), new Vector2Byte(1, 1));
        transformPool.Add(0, moverTransform);
        world.PlaceEntityOnMap(0, moverTransform.Position, ref moverTransform);
        actionLockPool.Add(0, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        // Deliberately different from FramesToWaitIfNoOptions (10) below -- a successful move
        // now sets the action lock to ActionCooldownFrames too, so if this used the same value
        // as the stuck-fallback, the two outcomes would be indistinguishable by lock value alone.
        movementPool.Add(0, new MovementComponent(MovementMode.Random, 5, null, null));
        nonBlockingPool.Add(0, new NonBlockingComponent());

        var southBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(1, southBlockerTransform);
        world.PlaceEntityOnMap(1, new Vector3Int(0, 1, 0), ref southBlockerTransform);

        var westBlockerTransform = new TransformComponent(new Vector3Int(), new Vector2Byte(1, 1));
        transformPool.Add(2, westBlockerTransform);
        world.PlaceEntityOnMap(2, new Vector3Int(1, 0, 0), ref westBlockerTransform);

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(new Random(1)), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
        system.Update(default, 0);

        Assert.AreEqual(5, actionLockPool.GetReadonly(0).LockFramesRemaining);
        Assert.AreNotEqual(new Vector3Int(0, 0, 0), transformPool.GetReadonly(0).Position);
    }

    /// <summary>
    /// Mirrors MapWindow.TryQueuePlayerMove's validate-then-queue pattern (on-map + free-space
    /// check before ever setting NextMapPosition -- SetNextMapPosition has no case for
    /// PlayerControlled, so MovementSystem itself never re-validates a queued target) applied
    /// to two independent entities, proving MovementMode.PlayerControlled isn't tied to any
    /// single global "the player": World.PlayerEntityId is just which PlayerControlled entity
    /// MapWindow's input happens to drive today, not a constraint MovementSystem enforces.
    /// Entity ids 0 and 15 share stripe bucket 0 (StripeCount = 15), so a single
    /// Update(_, 0) call processes both in the same tick.
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

        var system = new MovementSystem(transformPool, actionLockPool, movementPool, world, new MathUtility(), new EventBus(), new WorldEventSync(world), new FrameEventBuffer<EntityMoved>(), null);
        system.Update(default, 0);

        Assert.AreEqual(new Vector3Int(3, 2, 0), transformPool.GetReadonly(firstEntityId).Position, "First entity moves to its own valid target.");
        Assert.AreEqual(new Vector3Int(7, 7, 0), transformPool.GetReadonly(secondEntityId).Position, "Second entity stays put -- it never had a valid queued move.");
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
