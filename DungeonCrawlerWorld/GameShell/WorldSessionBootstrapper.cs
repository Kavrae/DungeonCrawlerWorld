using Engine.Content;
using Engine.Diagnostics;
using Engine.Math;
using Engine.Persistence;
using Engine.Settings;
using Game.Bootstrap;
using Game.Content;
using Game.Diagnostics;
using Game.Floors;
using Game.World;

namespace DungeonCrawlerWorld;

/// <summary>
/// Builds the world/simulation session -- validates the mods, builds the map, builds every ECS
/// module and the World over that map via GameBootstrapper, then populates the floor and spawns
/// the player. Composition-root-specific orchestration (which floor, the
/// Crawler-number range, where mods live) that GameBootstrapper itself deliberately stays
/// ignorant of -- see its own doc comment ("GameLoop calls this and supplies only the runtime
/// pieces it uniquely owns"). Lives in DungeonCrawlerWorld, not Game, for the same reason
/// ShellBootstrapper lives here rather than in Presentation: this is the concrete app's own
/// composition step, not a reusable layer.
/// </summary>
public static class WorldSessionBootstrapper
{
    /// <summary>The save a game uses: requested, the one "--save=" named, or a new game's own (SaveFolder.NewGameName) when it named none or a name a folder can't have, which is reported.</summary>
    public static string SaveNameFor(string? requested, int randomSeed)
    {
        if (requested is not null && SaveFolder.IsValidName(requested))
        {
            return requested;
        }

        if (requested is not null)
        {
            Console.Error.WriteLine($"[Save] '{requested}' can't be a save's name, so this game makes a new save.");
        }

        return SaveFolder.NewGameName(DateTime.Now, randomSeed);
    }

    /// <param name="playerActivityLogTarget">Where PlayerActivityLog writes, and how long StartupCleanup keeps its files before anything is built.</param>
    /// <param name="modsDirectory">The mods to load -- PlayerFolders.Mods for a game -- or null for none.</param>
    /// <param name="randomSeed">The session seed: neighborhood seeds are derived from it (NeighborhoodSeeds) and every gameplay roll draws from a stream derived from it (GameModuleContext.RandomStreams) -- see RandomSeed.</param>
    /// <param name="settingsSources">Where setting overrides come from, in order.</param>
    /// <param name="mapSizeOverride">Square map width and height from "--map-size=", or null for FloorBuilder's default -- see MapSizeArgument.</param>
    /// <param name="saveName">The save under PlayerFolders.Saves the session reads its definitions from and creates them in, made if it doesn't exist; null for a session with no save.</param>
    public static WorldSessionContext Build(
        int floorNumber,
        string? modsDirectory,
        int initialEntityCapacity,
        int initialComponentCapacity,
        int minCrawlerNumber,
        int crawlerNumberBits,
        RollingLogTarget playerActivityLogTarget,
        int randomSeed,
        IReadOnlyList<ISettingsSource> settingsSources,
        int? mapSizeOverride = null,
        string? saveName = null)
    {
        var save = saveName is null ? null : SaveFolder.OpenOrCreate(PlayerFolders.Saves, saveName);
        try
        {
            return BuildSession(floorNumber, modsDirectory, initialEntityCapacity, initialComponentCapacity, minCrawlerNumber, crawlerNumberBits, playerActivityLogTarget, randomSeed, settingsSources, mapSizeOverride, save);
        }
        catch
        {
            save?.Dispose();
            throw;
        }
    }

    /// <summary>Build, over save once it is open: the caller disposes save when this throws.</summary>
    private static WorldSessionContext BuildSession(
        int floorNumber,
        string? modsDirectory,
        int initialEntityCapacity,
        int initialComponentCapacity,
        int minCrawlerNumber,
        int crawlerNumberBits,
        RollingLogTarget playerActivityLogTarget,
        int randomSeed,
        IReadOnlyList<ISettingsSource> settingsSources,
        int? mapSizeOverride,
        SaveFolder? save)
    {
        using (EngineHooks.DiagnosticScope("Startup Cleanup"))
        {
            foreach (var line in StartupCleanup.Run([playerActivityLogTarget], save).ReportLines())
            {
                Console.WriteLine(line);
            }
        }

        var crawlerNumberAllocator = new UniqueNumberAllocator(RandomStreams.SeedFor(randomSeed, Game.Spawning.EntityFactory.CrawlerNumberStream), minCrawlerNumber, crawlerNumberBits);
        var neighborhoodRecords = new NeighborhoodRecords(randomSeed, floorNumber);
        var content = GameContent.MasterOnly(GameContent.DefaultMasterDirectory);

        ValidatedMods validatedMods;
        using (EngineHooks.DiagnosticScope("Mod Validation"))
        {
            validatedMods = ModValidation.Validate(modsDirectory, content, settingsSources);
        }

        Map map;
        using (EngineHooks.DiagnosticScope("World/Map Build"))
        {
            map = FloorBuilder.CreateMap(floorNumber, mapSizeOverride);
        }

        GameSession gameSession;
        using (EngineHooks.DiagnosticScope("Module Load"))
        {
            gameSession = GameBootstrapper.Build(validatedMods, map, content, randomSeed, initialEntityCapacity, initialComponentCapacity, crawlerNumberAllocator, settingsSources: settingsSources, save: save);
        }

        var ecsContext = gameSession.EcsContext;
        var world = gameSession.World;

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);
        var reservedEntityIds = FloorBuilder.ReserveTradeOfferEntities(ecsContext);


        foreach (var failure in gameSession.ModuleFailures)
        {
            Console.Error.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        foreach (var failure in gameSession.SettingsFailures)
        {
            Console.Error.WriteLine($"[Settings] {failure.Source}: {failure.Setting}: {failure.Message}");
        }

        if (save is not null)
        {
            CheckSaveHeader(save, gameSession, content.Layers[0], randomSeed);
        }

        ReportContentLayers(gameSession, save);
        foreach (var failure in gameSession.ContentFailures)
        {
            Console.Error.WriteLine($"[Content] {failure}");
        }

        // Must subscribe (in its own constructor) before CreatePlayer below publishes the
        // player's spawn EntityMovedEvent -- PlayerActivityLog's own spawn-time log line depends
        // on that immediate EventBus.Publish, not the buffered movedEntities.Record alongside it
        // (see FloorBuilder.CreatePlayer's own comment on why both exist). Population itself
        // (PopulateFloor, just below) never publishes EntityMovedEvent this way -- only the
        // buffered path -- so subscribing this early doesn't log anything spurious.
        var playerActivityLog = new PlayerActivityLog(world, ecsContext.ComponentManager, ecsContext.EventBus, ecsContext.Clock, playerActivityLogTarget.BasePath, gameSession.Catalogs.Definitions, gameSession.Views.ActionSourceNaming);
        Console.WriteLine($"[PlayerActivityLog] Writing to {playerActivityLog.FilePath}");

        // The tier reference is set to where the player is aimed at spawning BEFORE population, so
        // terrain and NPCs are born with their processing tier as their first component instead of
        // being tiered and then migrated. The player lands on the nearest free cell to this after
        // population; ProcessingTierSystem's first update treats any difference as an ordinary
        // player move and walks the Local boundary.
        var tierResolver = gameSession.Internals.ProcessingTierResolver;
        var playerSpawnOrigin = FloorBuilder.PlayerSpawnOrigin(gameSession.Catalogs.Areas);
        tierResolver.SetReferencePosition(playerSpawnOrigin);
        if (!world.Map.IsBounded)
        {
            tierResolver.SetWindowCenter(Game.World.Neighborhoods.CellOf(playerSpawnOrigin.X), Game.World.Neighborhoods.CellOf(playerSpawnOrigin.Y));
        }

        using (EngineHooks.DiagnosticScope("Entity Population"))
        {
            FloorBuilder.PopulateFloor(world, ecsContext, neighborhoodRecords, gameSession.Internals.Factory, gameSession.Catalogs.Terrain, gameSession.Catalogs.Auras, gameSession.Catalogs.Definitions, gameSession.Catalogs.Areas, gameSession.Internals.UniqueEntities);
        }

        using (EngineHooks.DiagnosticScope("Player Spawn"))
        {
            FloorBuilder.CreatePlayer(world, ecsContext, randomSeed, gameSession.Internals.Factory, gameSession.Catalogs.Definitions, playerEntityId, playerSpawnOrigin, tierResolver);
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
        // before the first frame. What only built creatures hold starts at the one simulated
        // neighborhood and settles at about three: a creature stays built until its neighborhood
        // unloads, and promotions wait for evictions' built creatures (ProcessingTierResolver.PromotionsHeld), so a walk keeps the
        // last two centres built plus the one being promoted. Without that reserve, each of the
        // first shifts resized the largest pools mid-drain -- one 30-60 ms frame per shift.
        if (!world.Map.IsBounded)
        {
            using (EngineHooks.DiagnosticScope("Window Headroom"))
            {
                const int windowNeighborhoods = 9;
                const int peakBuiltNeighborhoods = 3;
                var windowHeadroom = (windowNeighborhoods + NeighborhoodStreamer.CacheSize) / (double)windowNeighborhoods * 1.1;
                var builtHeadroom = peakBuiltNeighborhoods * 1.1;
                ecsContext.EntityManager.ReserveCapacity((int)(ecsContext.EntityManager.LivingEntityCount * windowHeadroom));
                ecsContext.ComponentManager.ReserveHeadroom(componentType =>
                    Game.Spawning.EntityFactory.SkeletonComponentTypes.Contains(componentType) ? windowHeadroom : builtHeadroom);
            }
        }

        // First in the frame: its population records spawns into the moved-entities buffer the other
        // systems read later the same frame.
        var neighborhoodStreamer = new NeighborhoodStreamer(world, ecsContext.EntityManager, ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>(), ecsContext.EventBus, tierResolver, neighborhoodRecords,
            new Game.TestMapBuilder(ecsContext.EntityManager, gameSession.Internals.Factory, gameSession.Catalogs.Terrain, gameSession.Catalogs.Auras, gameSession.Catalogs.Definitions, gameSession.Catalogs.Areas, gameSession.Internals.UniqueEntities), gameSession.Internals.Skeletons);
        ecsContext.SystemManager.RegisterAtFrameStart(neighborhoodStreamer, GameFrameStartOrder.NeighborhoodStreaming);
        RegisterStreamerGauges(ecsContext.Gauges, neighborhoodStreamer);

        using (EngineHooks.DiagnosticScope("Heap Compaction"))
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        // The terrain is all there, so the aura field scans it now rather than on the first frame or
        // the first draw, whichever came first: a startup step with a cost of its own (the scope is the
        // field's). After the compaction, not before: built while population's garbage was still held,
        // the field's own growth raised the peak working set by about 210 MB.
        gameSession.Internals.AuraField.EnsureBuilt();

        ecsContext.BeginSession();

        return new WorldSessionContext(gameSession, playerActivityLog, new Game.Admin.AdminTools(gameSession, neighborhoodStreamer), reservedEntityIds, save);
    }

    /// <summary>Writes a new save's save.json; for one that has it, reports each way this game differs from the one that made it (another master, a module at another content version), and nothing more.</summary>
    private static void CheckSaveHeader(SaveFolder save, GameSession gameSession, ContentLayer masterLayer, int randomSeed)
    {
        var current = new SaveHeader(SaveHeader.CurrentFormatVersion, DateTimeOffset.UtcNow, randomSeed, masterLayer.ComputeContentHash(), gameSession.Catalogs.ContentTypes.ModuleContentVersions);
        if (save.HeaderProblem is { } problem)
        {
            Console.Error.WriteLine($"[Save] {save.HeaderPath} can't be read, so it is left as it is: {problem}");
            return;
        }

        if (save.Header is not { } saved)
        {
            save.WriteHeader(current);
            Console.WriteLine($"[Save] New save {save.Name} at {save.Directory}");
            return;
        }

        Console.WriteLine($"[Save] Continuing save {save.Name} at {save.Directory}");
        foreach (var difference in saved.DifferencesFrom(current))
        {
            Console.Error.WriteLine($"[Save] {save.Name} differs from this game: {difference}");
        }
    }

    /// <summary>One line per content layer: its kind and path, then for each table how many records it won and how many of those are loaded yet, left out, and -- for the save -- holds as drafts.</summary>
    private static void ReportContentLayers(GameSession gameSession, SaveFolder? save)
    {
        var content = gameSession.Content;
        foreach (var layer in content.Layers.Layers)
        {
            var tables = new List<string>();
            foreach (var tableName in GameContent.TableNames)
            {
                var (records, loaded, leftOut) = content.CountIn(layer, tableName);
                var drafts = save is not null && layer.Kind == ContentLayerKind.Save && tableName is not GameContent.AreasTable ? Game.Content.ContentStore.DraftCountIn(save, GameContent.FileNameOf(tableName)) : 0;
                var counts = records == loaded ? $"{tableName} {records} loaded" : $"{tableName} {records} found, {loaded} loaded";
                counts += leftOut > 0 ? $", {leftOut} left out" : string.Empty;
                counts += drafts > 0 ? $", {drafts} as drafts" : string.Empty;
                tables.Add(counts);
            }

            var label = layer.Name.StartsWith(layer.Kind.ToString(), StringComparison.Ordinal) ? layer.Name : $"{layer.Kind} {layer.Name}";
            Console.WriteLine($"[Content] {label} ({layer.Directory}): {string.Join("; ", tables)}");
        }
    }

    private static void RegisterStreamerGauges(GaugeRegistry gauges, NeighborhoodStreamer neighborhoodStreamer)
    {
        const string streamerGroupName = nameof(NeighborhoodStreamer);
        gauges.Register(streamerGroupName, "LoadJobs", GaugeKind.Level, () => neighborhoodStreamer.LoadJobCount);
        gauges.Register(streamerGroupName, "UnloadJobs", GaugeKind.Level, () => neighborhoodStreamer.UnloadJobCount);
        gauges.Register(streamerGroupName, "RegenerateJobs", GaugeKind.Level, () => neighborhoodStreamer.RegenerateJobCount);
        gauges.Register(streamerGroupName, "PlansAwaitingStart", GaugeKind.Level, () => neighborhoodStreamer.PlansAwaitingStartCount);
        gauges.Register(streamerGroupName, "PlansRunning", GaugeKind.Level, () => neighborhoodStreamer.PlansRunningCount);
        gauges.Register(streamerGroupName, "PlansReady", GaugeKind.Level, () => neighborhoodStreamer.PlansReadyCount);
        gauges.Register(streamerGroupName, "BudgetUnitsSpent", GaugeKind.Cumulative, () => neighborhoodStreamer.TotalBudgetUnitsSpent);
        gauges.Register(streamerGroupName, "EntitiesSpawned", GaugeKind.Cumulative, () => neighborhoodStreamer.TotalEntitiesSpawned);
        gauges.Register(streamerGroupName, "EntitiesDestroyed", GaugeKind.Cumulative, () => neighborhoodStreamer.TotalEntitiesDestroyed);
    }
}
