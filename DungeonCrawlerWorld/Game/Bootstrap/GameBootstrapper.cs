using Engine.Bootstrap;
using Engine.Content;
using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Math;
using Engine.Modules;
using Engine.Persistence;
using Engine.Settings;
using Game.Blueprints;
using Game.Content;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.BodyPartEffects;
using Game.Modules.Burning;
using Game.Modules.Class;
using Game.Modules.TerrainContacts;
using Game.Modules.Containers;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Currency;
using Game.Modules.Death;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles;
using Game.Modules.Lootboxes;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.NpcBehavior;
using Game.Modules.Paralysis;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Shops;
using Game.Modules.StatModifiers;
using Game.Modules.Auras;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.World;

namespace Game.Bootstrap;

/// <summary>
/// Builds a game session from the built-in modules and a set of already validated mods (see
/// ModValidation): the session's own build over its map, and the Staging build SpawnRecordRebuilder
/// rebuilds creatures in, each a separate GameBuildPass. This is the actual composition point for
/// "which modules exist" -- GameLoop calls this and supplies only the runtime pieces it uniquely owns
/// (the map, the session seed, the validated mods), never naming a module by type.
/// </summary>
public static class GameBootstrapper
{
    /// <summary>A factory for every module the game ships with, in their default order.</summary>
    public static IReadOnlyList<ModuleFactory<GameModuleContext>> BuiltInModules() =>
    [
        ModuleFactory<GameModuleContext>.For<Terrain.TerrainModule>(),
        ModuleFactory<GameModuleContext>.For<CoreModule>(),
        ModuleFactory<GameModuleContext>.For<HealthModule>(),
        ModuleFactory<GameModuleContext>.For<ManaModule>(),
        ModuleFactory<GameModuleContext>.For<NpcBehaviorModule>(),
        ModuleFactory<GameModuleContext>.For<MovementModule>(),
        ModuleFactory<GameModuleContext>.For<DeathModule>(),
        ModuleFactory<GameModuleContext>.For<ProcessingTierModule>(),
        ModuleFactory<GameModuleContext>.For<RaceModule>(),
        ModuleFactory<GameModuleContext>.For<ClassModule>(),
        ModuleFactory<GameModuleContext>.For<BlueprintsModule>(),
        ModuleFactory<GameModuleContext>.For<ActionsModule>(),
        ModuleFactory<GameModuleContext>.For<CoreActionsModule>(),
        ModuleFactory<GameModuleContext>.For<StatusEffectsModule>(),
        ModuleFactory<GameModuleContext>.For<StatModifiersModule>(),
        ModuleFactory<GameModuleContext>.For<AbilityScoresModule>(),
        ModuleFactory<GameModuleContext>.For<BodyPartEffectsModule>(),
        ModuleFactory<GameModuleContext>.For<BurningModule>(),
        ModuleFactory<GameModuleContext>.For<PoisonModule>(),
        ModuleFactory<GameModuleContext>.For<ParalysisModule>(),
        ModuleFactory<GameModuleContext>.For<TerrainContactsModule>(),
        ModuleFactory<GameModuleContext>.For<AurasModule>(),
        ModuleFactory<GameModuleContext>.For<AchievementModule>(),
        ModuleFactory<GameModuleContext>.For<CrawlerModule>(),
        ModuleFactory<GameModuleContext>.For<InventoryModule>(),
        ModuleFactory<GameModuleContext>.For<CoreItemsModule>(),
        ModuleFactory<GameModuleContext>.For<LootboxModule>(),
        ModuleFactory<GameModuleContext>.For<CurrencyModule>(),
        ModuleFactory<GameModuleContext>.For<ContainersModule>(),
        ModuleFactory<GameModuleContext>.For<ItemPilesModule>(),
        ModuleFactory<GameModuleContext>.For<ShopModule>(),
        ModuleFactory<GameModuleContext>.For<Areas.AreasModule>(),
    ];

    /// <summary>Builds the built-in modules with validatedMods' mods over map and content into a session, with the staging world its SpawnRecordRebuilder rebuilds creatures in.</summary>
    /// <param name="content">The content layers the session reads below its mods' -- master; each surviving mod's layer goes on top in load order (ValidatedMods.ContentLayers), then the save's (save), and the staging world reads the same.</param>
    /// <param name="sessionSeed">The seed every random stream of the session is derived from (GameModuleContext.RandomStreams).</param>
    /// <param name="collectsContentFailures">A tool's build (the IDE): content that fails is left out and reported in GameSession.ContentFailures instead of stopping the build.</param>
    /// <param name="buildsStagingWorldOnFirstRebuild">True for a host that may never rebuild a record (the IDE): the staging world waits for the first rebuild instead of being built now.</param>
    /// <param name="loadsContentById">Loads actions and items as they are asked for (GameModuleContext.LoadsContentById), as a game does; false to register them all up front, as a tool's build (collectsContentFailures) must.</param>
    /// <param name="save">The save the session reads its Definitions from, on top of content, and creates and overrides definitions in (GameSession.ContentStore); null for a session with none (the IDE, a trial, a test). A host compacts it first, at startup (Engine.Persistence.StartupCleanup).</param>
    public static GameSession Build(
        ValidatedMods validatedMods,
        Map map,
        ContentLayers content,
        int sessionSeed,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        IReadOnlyList<ISettingsSource>? settingsSources = null,
        bool buildsStagingWorldOnFirstRebuild = false,
        bool collectsContentFailures = false,
        SaveFolder? save = null,
        bool loadsContentById = true)
    {
        settingsSources ??= [];
        var builtInModules = BuiltInModules();
        foreach (var modLayer in validatedMods.ContentLayers)
        {
            content = content.With(modLayer);
        }

        if (save is not null)
        {
            content = content.With(save.DefinitionsLayer);
        }

        var session = GameBuildPass.Run(builtInModules, validatedMods.Mods, map, content, sessionSeed, settingsSources, initialEntityCapacity, initialComponentCapacity, crawlerNumbers, collectsContentFailures, loadsContentById);
        var context = session.Context;
        var spawnRecordRebuilder = new SpawnRecordRebuilder(
            () => BuildSpawnRecordRebuilderStaging(builtInModules, validatedMods.Mods, content, settingsSources, collectsContentFailures, loadsContentById),
            stagingKeys => new EntityBuilder(context.Definitions, context.Auras, context.Actions, context.Items, stagingKeys),
            buildsStagingWorldOnFirstRebuild);
        var contentStore = save is null ? null : new ContentStore(context, save);
        return new GameSession(session, validatedMods.Failures, spawnRecordRebuilder, CreateTeleporter(session.World, session.EcsContext, session.Factory, session.Factory.Skeletons), contentStore);
    }

    private static EntityTeleporter CreateTeleporter(World.World world, EcsContext ecsContext, EntityFactory factory, CreatureSkeletons skeletons)
    {
        var componentManager = ecsContext.ComponentManager;
        return new EntityTeleporter(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            factory.SpawnMoves,
            ecsContext.EventBus,
            skeletons,
            componentManager.GetPackedPool<Modules.Movement.Components.MovementComponent>(),
            componentManager.GetPackedPool<Modules.Actions.Components.PendingWindupComponent>());
    }

    /// <summary>A separate build of every module, as the staging world SpawnRecordRebuilder rebuilds creatures in.</summary>
    /// <remarks>
    /// Its own Staging build over a placeholder map, with streams of its own (session seed 0) so staging never draws
    /// from the session's, and none of what only a session reads. Its builds read the session's own
    /// BlueprintRegistry, so a spawn record means the same blueprint in both worlds, including one registered
    /// after this runs.
    /// </remarks>
    private static EcsContext BuildSpawnRecordRebuilderStaging(IReadOnlyList<ModuleFactory<GameModuleContext>> builtInModules, IReadOnlyList<ModuleFactory<GameModuleContext>> mods, ContentLayers content, IReadOnlyList<ISettingsSource> settingsSources, bool collectsContentFailures, bool loadsContentById)
    {
        using var stagingScope = EngineHooks.DiagnosticScope("BuildSpawnRecordRebuilderStaging");
        return GameBuildPass.BuildModules(builtInModules, mods, GameBuildPass.CreatePlaceholderMap(), content, sessionSeed: 0, settingsSources, initialEntityCapacity: 16, initialComponentCapacity: 16, purpose: EcsBuildPurpose.Staging, collectsContentFailures: collectsContentFailures, loadsContentById: loadsContentById).EcsContext;
    }
}
