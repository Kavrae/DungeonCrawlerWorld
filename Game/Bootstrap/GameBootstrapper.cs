using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Math;
using Engine.Modules;
using Engine.Settings;
using Game.Spawning;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.BodyPartEffects;
using Game.Modules.Burning;
using Game.Modules.Class;
using Game.Modules.ContactDamage;
using Game.Modules.Containers;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
using Game.Modules.Currency;
using Game.Modules.Death;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Mana;
using Game.Modules.Movement;
using Game.Modules.NpcBehavior;
using Game.Modules.Paralysis;
using Game.Modules.Poison;
using Game.Modules.ProcessingTier;
using Game.Modules.Race;
using Game.Modules.Shops;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

namespace Game.Bootstrap;

/// <summary>
/// Builds a game session from the built-in modules and a set of already validated mods (see
/// ModValidation): the session's own build over its map, and the staging world SpawnRecordRebuilder
/// rebuilds creatures in, each a separate GameBuildPass. This is the actual composition point for
/// "which modules exist" -- GameLoop calls this and supplies only the runtime pieces it uniquely owns
/// (the map, MathUtility, the validated mods), never naming a module by type.
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
        ModuleFactory<GameModuleContext>.For<ContactDamageModule>(),
        ModuleFactory<GameModuleContext>.For<StatusEffectAuraModule>(),
        ModuleFactory<GameModuleContext>.For<AchievementModule>(),
        ModuleFactory<GameModuleContext>.For<CrawlerModule>(),
        ModuleFactory<GameModuleContext>.For<InventoryModule>(),
        ModuleFactory<GameModuleContext>.For<CoreItemsModule>(),
        ModuleFactory<GameModuleContext>.For<CurrencyModule>(),
        ModuleFactory<GameModuleContext>.For<ContainersModule>(),
        ModuleFactory<GameModuleContext>.For<ShopModule>(),
    ];

    public static GameBootstrapResult Build(
        ValidatedMods validatedMods,
        Map map,
        MathUtility mathUtility,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSpawnSeed = 0,
        IReadOnlyList<ISettingsSource>? settingsSources = null)
    {
        settingsSources ??= [];
        var builtInModules = BuiltInModules();

        GameBuildPassResult staging;
        using (EngineHooks.DiagnosticScope("BuildSpawnRecordRebuilderStaging"))
        {
            staging = BuildSpawnRecordRebuilderStaging(builtInModules, validatedMods.Mods, settingsSources);
        }

        var session = GameBuildPass.Run(builtInModules, validatedMods.Mods, map, mathUtility, settingsSources, initialEntityCapacity, initialComponentCapacity, crawlerNumbers, runtimeSpawnSeed);
        var ecsContext = session.EcsContext;
        var context = session.Context;
        var factory = session.Factory;
        var skeletons = factory.Skeletons;

        return new GameBootstrapResult(ecsContext, session.World, validatedMods.Failures, session.Settings.Values, session.Settings.Failures, context.Actions, context.MovedEntities, context.Items, context.StatusEffectDisplays, session.LocalTierRoster, context.ProcessingTierResolver, context.Terrain, context.Definitions, new SpawnRecordRebuilder(staging.EcsContext, new EntityBuilder(context.Definitions, staging.EcsContext.EntityManager.Keys)), skeletons, factory, CreateTeleporter(session.World, ecsContext, factory, skeletons));
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
            componentManager.GetPackedPool<Modules.Actions.Components.PendingDelayedActionComponent>());
    }

    /// <summary>A separate build of every module, as the staging world SpawnRecordRebuilder rebuilds creatures in.</summary>
    /// <remarks>
    /// Its own GameBuildPass over a placeholder map, with its own random sequence so staging never draws
    /// from the session's. Its builds read the session's own BlueprintRegistry, so a spawn record means the
    /// same blueprint in both worlds, including one registered after this runs.
    /// </remarks>
    private static GameBuildPassResult BuildSpawnRecordRebuilderStaging(IReadOnlyList<ModuleFactory<GameModuleContext>> builtInModules, IReadOnlyList<ModuleFactory<GameModuleContext>> mods, IReadOnlyList<ISettingsSource> settingsSources) =>
        GameBuildPass.Run(builtInModules, mods, GameBuildPass.CreatePlaceholderMap(), new MathUtility(new SeededRandom()), settingsSources, initialEntityCapacity: 16, initialComponentCapacity: 16);
}
