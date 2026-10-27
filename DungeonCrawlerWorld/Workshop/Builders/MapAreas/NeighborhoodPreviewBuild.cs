using Engine.Math;
using Game;
using Game.Areas;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Core.Components;
using Game.World;

namespace Builders.MapAreas;

/// <summary>One starting-window neighborhood of a seeded world as the game generates it: TestMapBuilder.Plan run on the record and population seed the session would give it, applied to a session over a bounded map of that one neighborhood -- never simulated.</summary>
/// <remarks>
/// The record comes from FloorBuilder.StartingNeighborhoodRecords, the game's own assignment for the session seed, so
/// the preview is the neighborhood a game started with --seed=Seed first generates. The plan is the game's, area stamps
/// and all, spawned in its order by the game's TestMapBuilder; the tier reference is the player's spawn, as at startup.
/// </remarks>
public sealed class NeighborhoodPreviewBuild : IMapPreviewScene
{
    private const int LayerCount = 3;

    private NeighborhoodPreviewBuild(GameSession session, NeighborhoodRecord record, int populationSeed, NeighborhoodPlan plan, int seed)
    {
        Session = session;
        Record = record;
        PopulationSeed = populationSeed;
        Plan = plan;
        Seed = seed;
    }

    public GameSession Session { get; }

    public NeighborhoodRecord Record { get; }

    public int PopulationSeed { get; }

    public NeighborhoodPlan Plan { get; }

    /// <summary>The session seed the world is generated from.</summary>
    public int Seed { get; }

    public AreaStampResult Stamp => Plan.Areas;

    AreaInstance? IMapPreviewScene.Area => null;

    /// <exception cref="ArgumentOutOfRangeException">The cell isn't one of FloorBuilder.StartingWindowNeighborhoods.</exception>
    public static NeighborhoodPreviewBuild Build(int seed, int cellX, int cellY, Func<Map, GameSession> buildSession)
    {
        var (record, populationSeed) = FloorBuilder.StartingNeighborhoodRecords(seed, FloorBuilder.FirstFloorNumber)
            .FirstOrDefault(entry => entry.Record.CellX == cellX && entry.Record.CellY == cellY);
        if (record is null)
        {
            throw new ArgumentOutOfRangeException(nameof(cellX), $"({cellX}, {cellY}) isn't a starting-window neighborhood.");
        }

        var map = new Map(Neighborhoods.AreaOf(cellX, cellY, LayerCount));
        var session = buildSession(map);
        var catalogs = session.Catalogs;
        var internals = session.Internals;
        if (catalogs.Areas.TryFindPlayerSpawn(out var playerSpawn))
        {
            internals.ProcessingTierResolver.SetReferencePosition(playerSpawn);
        }

        var builder = new TestMapBuilder(session.EcsContext.EntityManager, internals.Factory, catalogs.Terrain, catalogs.Auras, catalogs.Definitions, catalogs.Areas, internals.UniqueEntities);
        var plan = builder.Plan(map, record, populationSeed);
        map.LoadNeighborhood(plan.Layout);
        foreach (var _ in builder.Spawn(plan))
        {
        }

        internals.AuraField.EnsureBuilt();
        return new NeighborhoodPreviewBuild(session, record, populationSeed, plan, seed);
    }

    /// <summary>Where the camera starts: the first stamped area's top-left, else the neighborhood's centre.</summary>
    public Vector3Int StartingFocus()
    {
        if (Stamp.Instances.FirstOrDefault() is { } instance)
        {
            return new Vector3Int(instance.OriginX + instance.Definition.Width / 2, instance.OriginY + instance.Definition.Height / 2, (int)MapLayer.Ground);
        }

        var origin = Neighborhoods.AreaOf(Record.CellX, Record.CellY, LayerCount);
        return new Vector3Int(origin.MinX + Neighborhoods.SizeTiles / 2, origin.MinY + Neighborhoods.SizeTiles / 2, (int)MapLayer.Ground);
    }
}
