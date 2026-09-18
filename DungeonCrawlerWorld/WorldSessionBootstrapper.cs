using Engine.Diagnostics;
using Engine.Math;
using Game.Bootstrap;
using Game.Diagnostics;
using Game.Floors;
using Game.World;

namespace DungeonCrawlerWorld;

/// <summary>
/// Builds the world/simulation session -- World, then (World must exist first, see
/// GameBootstrapper's own doc comment) every ECS module via GameBootstrapper, then populates the
/// floor and spawns the player. Composition-root-specific orchestration (which floor, the
/// Crawler-number range, where mods live) that GameBootstrapper itself deliberately stays
/// ignorant of -- see its own doc comment ("GameLoop calls this and supplies only the runtime
/// pieces it uniquely owns"). Lives in DungeonCrawlerWorld, not Game, for the same reason
/// ShellBootstrapper lives here rather than in Presentation: this is the concrete app's own
/// composition step, not a reusable layer.
/// </summary>
public static class WorldSessionBootstrapper
{
    /// <param name="randomSeed">Seed for the shared MathUtility this session's entire simulation draws from -- see RandomSeed and the body's own note on what it does and does not cover.</param>
    /// <param name="mapSizeOverride">Square map width and height from "--map-size=", or null for FloorBuilder's default -- see MapSizeArgument.</param>
    public static WorldSessionContext Build(
        int floorNumber,
        string modsDirectory,
        int initialEntityCapacity,
        int initialComponentCapacity,
        int minCrawlerNumber,
        int maxCrawlerNumber,
        string playerActivityLogFilePath,
        DiagnosticsEngine diagnostics,
        int randomSeed,
        int? mapSizeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var mathUtility = new MathUtility(new Random(randomSeed));
        var crawlerNumberAllocator = new UniqueNumberAllocator(mathUtility, minCrawlerNumber, maxCrawlerNumber);
        var neighborhoodRecords = new NeighborhoodRecords(mathUtility);

        World world;
        using (diagnostics.StartupProfiler?.Phase("World/Map Build"))
        {
            world = new World(FloorBuilder.CreateMap(floorNumber, mapSizeOverride));
        }

        GameBootstrapResult bootstrapResult;
        using (diagnostics.StartupProfiler?.Phase("Module Load"))
        {
            bootstrapResult = GameBootstrapper.Build(world, mathUtility, modsDirectory, initialEntityCapacity, initialComponentCapacity, diagnostics.StartupProfiler);
        }

        var ecsContext = bootstrapResult.EcsContext;

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        var reservedEntityIds = FloorBuilder.ReserveTradeOfferEntities(ecsContext);

        diagnostics.AttachEcsContext(ecsContext.ComponentManager, ecsContext.EntityManager);
        ecsContext.SystemManager.Profiler = diagnostics.FrameCostRecorder;
        ecsContext.EventBus.Profiler = diagnostics.FrameCostRecorder;

        foreach (var failure in bootstrapResult.Failures)
        {
            Console.Error.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        // Must subscribe (in its own constructor) before CreatePlayer below publishes the
        // player's spawn EntityMovedEvent -- PlayerActivityLog's own spawn-time log line depends
        // on that immediate EventBus.Publish, not the buffered movedEntities.Record alongside it
        // (see FloorBuilder.CreatePlayer's own comment on why both exist). Population itself
        // (PopulateFloor, just below) never publishes EntityMovedEvent this way -- only the
        // buffered path -- so subscribing this early doesn't log anything spurious.
        var playerActivityLog = new PlayerActivityLog(world, ecsContext.ComponentManager, ecsContext.EventBus, playerActivityLogFilePath);
        Console.WriteLine($"[PlayerActivityLog] Writing to {playerActivityLogFilePath}");

        // The tier reference is set to where the player is aimed at spawning BEFORE population, so
        // terrain and NPCs are born with their processing tier as their first component instead of
        // being tiered and then migrated. The player lands on the nearest free cell to this after
        // population; ProcessingTierSystem's first update treats any difference as an ordinary
        // player move and walks the Local boundary.
        var tierResolver = bootstrapResult.ProcessingTierResolver;
        tierResolver.SetReferencePosition(FloorBuilder.PlayerSpawnOrigin());
        if (!world.Map.IsBounded)
        {
            tierResolver.SetWindowCenter(Game.TestMapBuilder.StartingCellX, Game.TestMapBuilder.StartingCellY);
        }

        using (diagnostics.StartupProfiler?.Phase("Entity Population"))
        {
            FloorBuilder.PopulateFloor(world, ecsContext, neighborhoodRecords, crawlerNumberAllocator, bootstrapResult.MovedEntities, bootstrapResult.Terrain, tierResolver);
        }

        using (diagnostics.StartupProfiler?.Phase("Player Spawn"))
        {
            FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, bootstrapResult.MovedEntities, crawlerNumberAllocator, playerEntityId, tierResolver);
            world.PlayerEntityId = playerEntityId;

            ecsContext.EventBus.Publish(new EnteredDungeonEvent());
            ecsContext.EventBus.Publish(new FloorEnteredEvent(floorNumber));
        }

        // Population grows every pool by doubling, and the GC keeps the heap it grew to long after
        // the old arrays are garbage -- on the 3x3 map that was ~6 GB of working set holding ~2.4 GB
        // of live data. One aggressive, compacting collection here, before the first frame, hands
        // the rest back.
        // The sliding window holds its 9 neighborhoods plus a cache of recently dropped ones, so the
        // population a session settles at is that much larger than the 9 it starts with. Reserving it
        // now, with 10% to spare, moves every pool's growth from the first walks across the map to
        // before the first frame.
        if (!world.Map.IsBounded)
        {
            using (diagnostics.StartupProfiler?.Phase("Window Headroom"))
            {
                const int windowNeighborhoods = 9;
                var headroom = (windowNeighborhoods + NeighborhoodStreamer.CacheSize) / (double)windowNeighborhoods * 1.1;
                ecsContext.EntityManager.ReserveCapacity((int)(ecsContext.EntityManager.LivingEntityCount * headroom));
                ecsContext.ComponentManager.ReserveHeadroom(headroom);
            }
        }

        // First in the frame: its population records spawns into the moved-entities buffer the other
        // systems read later the same frame.
        var neighborhoodStreamer = new NeighborhoodStreamer(world, ecsContext.EntityManager, ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>(), ecsContext.EventBus, tierResolver, neighborhoodRecords,
            new Game.TestMapBuilder(ecsContext.EntityManager, ecsContext.ComponentManager, crawlerNumberAllocator, bootstrapResult.MovedEntities, bootstrapResult.Terrain, tierResolver, ecsContext.SystemManager.Clock));
        ecsContext.SystemManager.RegisterFirst(neighborhoodStreamer);

        using (diagnostics.StartupProfiler?.Phase("Heap Compaction"))
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        return new WorldSessionContext(world, ecsContext, mathUtility, bootstrapResult.MovedEntities, crawlerNumberAllocator, bootstrapResult.ActionCatalog, bootstrapResult.ItemCatalog, playerActivityLog, bootstrapResult.StatusEffectDisplays, reservedEntityIds, bootstrapResult.LocalTierRoster, bootstrapResult.Terrain, neighborhoodRecords, neighborhoodStreamer);
    }
}
