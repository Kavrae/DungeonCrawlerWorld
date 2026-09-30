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
        var mathUtility = new MathUtility(new Random(1));
        gameSession ??= GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), mathUtility, initialEntityCapacity: 100, initialComponentCapacity: 50);

        var ecsContext = gameSession.EcsContext;
        var internals = gameSession.Internals;
        var neighborhoodStreamer = new NeighborhoodStreamer(
            gameSession.World,
            ecsContext.EntityManager,
            ecsContext.ComponentManager.GetDirectPool<TransformComponent>(),
            ecsContext.EventBus,
            internals.ProcessingTierResolver,
            new NeighborhoodRecords(mathUtility),
            new TestMapBuilder(ecsContext.EntityManager, internals.Factory, gameSession.Catalogs.Terrain, gameSession.Catalogs.Definitions),
            internals.Skeletons);

        return new AdminTools(gameSession, neighborhoodStreamer);
    }
}
