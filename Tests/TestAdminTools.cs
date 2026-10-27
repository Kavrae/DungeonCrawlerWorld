using Engine.Math;
using Game;
using Game.Admin;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests;

/// <summary>Builds a real AdminTools for a test, over a small session of its own unless one is given.</summary>
internal static class TestAdminTools
{
    public static AdminTools Create(GameSession? gameSession = null)
    {
        gameSession ??= GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), sessionSeed: 1, initialEntityCapacity: 100, initialComponentCapacity: 50);

        var ecsContext = gameSession.EcsContext;
        var internals = gameSession.Internals;
        var neighborhoodStreamer = new NeighborhoodStreamer(
            gameSession.World,
            ecsContext.EntityManager,
            ecsContext.ComponentManager.GetDirectPool<TransformComponent>(),
            ecsContext.EventBus,
            internals.ProcessingTierResolver,
            new NeighborhoodRecords(worldSeed: 1, floorNumber: FloorBuilder.FirstFloorNumber),
            new TestMapBuilder(ecsContext.EntityManager, internals.Factory, gameSession.Catalogs.Terrain, gameSession.Catalogs.Auras, gameSession.Catalogs.Definitions, gameSession.Catalogs.Areas, internals.UniqueEntities),
            internals.Skeletons);

        return new AdminTools(gameSession, neighborhoodStreamer);
    }
}
