using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class EntityTeleporterTests
{
    private const int Rows = 24;

    private sealed record Session(Game.World.World World, EcsContext Ecs, GameSession Result, int PlayerEntityId)
    {
        public EntityTeleporter Teleporter => Result.Internals.Teleporter;

        public Vector3Int PositionOf(int entityId) => Ecs.ComponentManager.GetDirectPool<TransformComponent>().GetReadonly(entityId).Position;
    }

    /// <summary>Three neighborhoods side by side (-1, 0, 1), the window centred on 0 with the player in it.</summary>
    private static Session BuildSession()
    {
        var map = new Map(new MapBounds(-Neighborhoods.SizeTiles, 0, 2 * Neighborhoods.SizeTiles, Rows, 3));
        var mathUtility = new MathUtility(new Random(1));
        var result = GameBootstrapper.Build(ValidatedMods.None, map, mathUtility, initialEntityCapacity: 1_000, initialComponentCapacity: 100, crawlerNumbers: new UniqueNumberAllocator(1, 1, 24));
        var world = result.World;
        var ecs = result.EcsContext;
        var resolver = result.Internals.ProcessingTierResolver;
        resolver.SetReferencePosition(FloorBuilder.PlayerSpawnOrigin());
        resolver.SetWindowCenter(0, 0);

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecs);
        FloorBuilder.PopulateFloor(world, ecs, new NeighborhoodRecords(mathUtility), result.Internals.Factory, result.Catalogs.Terrain, result.Catalogs.Auras, result.Catalogs.Definitions);
        FloorBuilder.CreatePlayer(world, ecs, mathUtility, result.Internals.Factory, result.Catalogs.Definitions, playerEntityId, resolver);
        world.PlayerEntityId = playerEntityId;
        result.Internals.MovedEntities.ClearFrame();

        return new Session(world, ecs, result, playerEntityId);
    }

    private static Vector3Int FreeCellNear(Session session, int x, int y) =>
        FloorBuilder.FindFreeGroundCellNear(session.World, new Vector3Int(x, y, (int)MapLayer.Ground));

    private static void RunFrames(Session session, int count)
    {
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var start = session.Ecs.SystemManager.Clock.CurrentFrame + 1;
        for (var frame = start; frame < start + count; frame++)
        {
            session.Ecs.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
        }
    }

    [TestMethod]
    public void TryTeleport_MovesTheEntityAndItsMapFootprint()
    {
        var session = BuildSession();
        var origin = session.PositionOf(session.PlayerEntityId);
        var destination = FreeCellNear(session, origin.X + 300, origin.Y);

        Assert.IsTrue(session.Teleporter.TryTeleport(session.PlayerEntityId, destination));

        Assert.AreEqual(destination, session.PositionOf(session.PlayerEntityId));
        Assert.AreEqual(session.PlayerEntityId, session.World.GetEntityIdAt(destination));
        Assert.AreNotEqual(session.PlayerEntityId, session.World.GetEntityIdAt(origin));
    }

    [TestMethod]
    public void TryTeleport_RecordsTheMoveForEveryReader()
    {
        var session = BuildSession();
        var origin = session.PositionOf(session.PlayerEntityId);
        var destination = FreeCellNear(session, origin.X + 300, origin.Y);

        session.Teleporter.TryTeleport(session.PlayerEntityId, destination);

        CollectionAssert.Contains(session.Result.Internals.MovedEntities.Items.ToList(), new EntityMovedEvent(session.PlayerEntityId, origin, destination, new Vector2Byte(1, 1)));
    }

    [TestMethod]
    public void TryTeleport_RefusesAnOccupiedCell_AndChangesNothing()
    {
        var session = BuildSession();
        var origin = session.PositionOf(session.PlayerEntityId);
        var occupied = FindBlockingOccupant(session);

        Assert.IsFalse(session.Teleporter.TryTeleport(session.PlayerEntityId, occupied));

        Assert.AreEqual(origin, session.PositionOf(session.PlayerEntityId));
        Assert.AreEqual(session.PlayerEntityId, session.World.GetEntityIdAt(origin));
        Assert.IsEmpty(session.Result.Internals.MovedEntities.Items);
    }

    [TestMethod]
    public void TryTeleport_RefusesACellOffTheMap()
    {
        var session = BuildSession();
        var origin = session.PositionOf(session.PlayerEntityId);

        Assert.IsFalse(session.Teleporter.TryTeleport(session.PlayerEntityId, new Vector3Int(origin.X, Rows + 10, origin.Z)));

        Assert.AreEqual(origin, session.PositionOf(session.PlayerEntityId));
    }

    [TestMethod]
    public void TryTeleport_DropsTheMovementDestination_AndCancelsAWindup()
    {
        var session = BuildSession();
        var componentManager = session.Ecs.ComponentManager;
        var origin = session.PositionOf(session.PlayerEntityId);
        componentManager.GetPackedPool<MovementComponent>().TryUpdate(session.PlayerEntityId, (ref MovementComponent movement) =>
        {
            movement.TargetMapPosition = origin with { X = origin.X + 5 };
            movement.NextMapPosition = origin with { X = origin.X + 1 };
        });
        componentManager.Merge(session.PlayerEntityId, new PendingDelayedActionComponent(Guid.NewGuid(), [origin with { X = origin.X + 1 }], 1_000));

        session.Teleporter.TryTeleport(session.PlayerEntityId, FreeCellNear(session, origin.X + 300, origin.Y));

        var movement = componentManager.GetPackedPool<MovementComponent>().GetReadonly(session.PlayerEntityId);
        Assert.IsNull(movement.TargetMapPosition);
        Assert.IsNull(movement.NextMapPosition);
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(session.PlayerEntityId));
    }

    [TestMethod]
    public void TryTeleport_BuildsASkeletonFirst()
    {
        var session = BuildSession();
        var skeleton = FirstSkeleton(session);
        var position = session.PositionOf(skeleton);

        Assert.IsTrue(session.Teleporter.TryTeleport(skeleton, FreeCellNear(session, position.X + 3, position.Y)));

        Assert.IsFalse(session.Result.Internals.Skeletons.IsSkeleton(skeleton));
    }

    [TestMethod]
    public void TryTeleport_IntoTheNextNeighborhood_ShiftsTheWindowAndTheShiftSettles()
    {
        var session = BuildSession();
        var resolver = session.Result.Internals.ProcessingTierResolver;

        session.Teleporter.TryTeleport(session.PlayerEntityId, FreeCellNear(session, Neighborhoods.OriginOf(1) + 200, 5));
        RunFrames(session, 1);

        Assert.AreEqual((1, 0), resolver.WindowCenter);
        Assert.IsTrue(resolver.Transitions.HasPending);

        for (var i = 0; i < 500 && resolver.Transitions.HasPending; i++)
        {
            RunFrames(session, 1);
        }

        Assert.IsFalse(resolver.Transitions.HasPending);
    }

    private static Vector3Int FindBlockingOccupant(Session session)
    {
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (entityId != session.PlayerEntityId &&
                transforms.TryGetReadonly(entityId, out var transform) &&
                session.World.IsOnMap(transform.Position) &&
                session.World.GetEntityIdAt(transform.Position) == entityId)
            {
                return transform.Position;
            }
        }

        throw new InvalidOperationException("No blocking entity on the map.");
    }

    private static int FirstSkeleton(Session session)
    {
        var transforms = session.Ecs.ComponentManager.GetDirectPool<TransformComponent>();
        for (var entityId = 0; entityId < transforms.Capacity; entityId++)
        {
            if (session.Result.Internals.Skeletons.IsSkeleton(entityId))
            {
                return entityId;
            }
        }

        throw new InvalidOperationException("No skeleton on the map.");
    }
}
