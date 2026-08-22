using Engine.Diagnostics;
using Engine.Math;
using Game.Bootstrap;
using Game.Diagnostics;
using Game.Floors;
using Game.Notifications;
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
    public static WorldSessionContext Build(
        int floorNumber,
        string modsDirectory,
        int initialEntityCapacity,
        int initialComponentCapacity,
        int minCrawlerNumber,
        int maxCrawlerNumber,
        string playerActivityLogFilePath,
        DiagnosticsEngine diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var mathUtility = new MathUtility();
        var crawlerNumberAllocator = new UniqueNumberAllocator(mathUtility, minCrawlerNumber, maxCrawlerNumber);

        World world;
        using (diagnostics.StartupProfiler?.Phase("World/Map Build"))
        {
            world = new World(FloorBuilder.CreateMap(floorNumber));
        }

        GameBootstrapResult bootstrapResult;
        using (diagnostics.StartupProfiler?.Phase("Module Load"))
        {
            bootstrapResult = GameBootstrapper.Build(world, mathUtility, modsDirectory, initialEntityCapacity, initialComponentCapacity, diagnostics.StartupProfiler);
        }

        var ecsContext = bootstrapResult.EcsContext;

        var playerEntityId = FloorBuilder.ReservePlayerEntity(ecsContext);

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

        using (diagnostics.StartupProfiler?.Phase("Entity Population"))
        {
            FloorBuilder.PopulateFloor(world, ecsContext, mathUtility, crawlerNumberAllocator, bootstrapResult.MovedEntities);
        }

        // The welcome notification reacts to EnteredDungeonEvent the same way every achievement
        // trigger does (see AchievementTriggerContext.SubscribeUntilTriggered), publishing
        // NotificationRequestedEvent instead of a direct NotificationCenter reference -- this
        // runs well before ShellBootstrapper.Build ever constructs one. Subscribed before
        // CreatePlayer publishes the event below, same ordering requirement any subscriber has.
        ecsContext.EventBus.Subscribe<EnteredDungeonEvent>(_ =>
            ecsContext.EventBus.Publish(new NotificationRequestedEvent(NotificationCategory.System, "Welcome to the World Dungeon!", ShowImmediately: true)));

        using (diagnostics.StartupProfiler?.Phase("Player Spawn"))
        {
            FloorBuilder.CreatePlayer(world, ecsContext, mathUtility, bootstrapResult.MovedEntities, crawlerNumberAllocator, playerEntityId);
            world.PlayerEntityId = playerEntityId;

            ecsContext.EventBus.Publish(new EnteredDungeonEvent());
            ecsContext.EventBus.Publish(new FloorEnteredEvent(floorNumber));
        }

        return new WorldSessionContext(world, ecsContext, mathUtility, bootstrapResult.MovedEntities, crawlerNumberAllocator, bootstrapResult.ActionCatalog, bootstrapResult.ItemCatalog, playerActivityLog);
    }
}
