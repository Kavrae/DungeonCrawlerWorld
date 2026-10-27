using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Math;
using Engine.Modules;
using Engine.Settings;
using Game.Areas;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Auras;
using Game.Spawning;
using Game.Tags;
using Game.Terrain;
using Game.World;

namespace Game.Bootstrap;

/// <summary>One complete, self-contained build of the game's modules: fresh module instances, its own World over its own Map, and its own EventBus, entity keys and gameModuleContext.</summary>
/// <remarks>
/// Every build goes through here -- the real session, the SpawnRecordRebuilder staging world, each mod's dry run and
/// a test's few modules -- so nothing one build creates is reachable from another, and none depends on running in a
/// particular order relative to the others. EcsBuilder runs the phases; this supplies the game's context and checks.
/// </remarks>
public static class GameBuildPass
{
    /// <summary>A bounded map with one tile per layer, for a build that never places anything: a mod's dry run, or the staging world.</summary>
    public static Map CreatePlaceholderMap() => new(new Vector3Int(1, 1, Enum.GetValues<MapLayer>().Length));

    /// <summary>Builds builtInModules with mods combined into them (a mod replaces the built-in sharing its Id), over map, with everything a game session reads.</summary>
    /// <param name="sessionSeed">The seed every random stream of the build is derived from (GameModuleContext.RandomStreams); a build that is never played passes 0.</param>
    /// <param name="crawlerNumbers">The numbers a crawler draws when first built; null for a build that never builds one.</param>
    /// <param name="collectsContentFailures">A tool's build: content that fails is left out and reported in GameModuleContext.ContentFailures instead of stopping the build.</param>
    public static GameBuildPassResult Run(
        IReadOnlyList<ModuleFactory<GameModuleContext>> builtInModules,
        IReadOnlyList<ModuleFactory<GameModuleContext>> mods,
        Map map,
        int sessionSeed,
        IReadOnlyList<ISettingsSource> settingsSources,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        bool collectsContentFailures = false)
    {
        var build = BuildModules(builtInModules, mods, map, sessionSeed, settingsSources, initialEntityCapacity, initialComponentCapacity, crawlerNumbers, collectsContentFailures: collectsContentFailures);
        var ecsContext = build.EcsContext;
        var context = build.Context;
        var componentManager = ecsContext.ComponentManager;

        var localTierRoster = new LocalTierRoster(componentManager.GetPackedPool<MovementComponent>(), componentManager.GetDirectPool<ProcessingTierComponent>(), context.ProcessingTierEvents);

        RegisterDiagnostics(ecsContext, context.EntityFactory.Skeletons, localTierRoster, context.ProcessingTierResolver, context.AuraField);

        return new GameBuildPassResult(ecsContext, build.World, context, build.Modules, build.Declarations, localTierRoster);
    }

    /// <summary>Builds modules with mods combined into them over map: the game's context, its checks on what the modules configured, and the wiring every build needs.</summary>
    /// <remarks>
    /// The module build without what only a whole game session reads (Run's roster and diagnostics), so it serves a
    /// module set that isn't a whole game -- a test of a few modules -- under the same clock, tier policy and wiring as
    /// the game, and every build that is never simulated: a mod's trial and the staging world (purpose). modules must
    /// still contain every one of GameModuleContext.FoundationModuleIds.
    /// </remarks>
    /// <param name="purpose">What the build is for; a Trial or Staging build can never begin a session.</param>
    /// <exception cref="InvalidOperationException">modules lacks a foundation module, or fails EcsBuilder's own checks or the content checks.</exception>
    public static GameModuleBuild BuildModules(
        IReadOnlyList<ModuleFactory<GameModuleContext>> modules,
        IReadOnlyList<ModuleFactory<GameModuleContext>> mods,
        Map map,
        int sessionSeed,
        IReadOnlyList<ISettingsSource> settingsSources,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        EcsBuildPurpose purpose = EcsBuildPurpose.Session,
        bool collectsContentFailures = false)
    {
        World.World? world = null;

        var build = EcsBuilder.Build(new EcsBuildRequest<GameModuleContext>
        {
            Purpose = purpose,
            Modules = modules,
            Mods = mods,
            SettingsSources = settingsSources,
            FramesPerSecond = GameFrameRate.FramesPerSecond,
            InitialEntityCapacity = initialEntityCapacity,
            InitialComponentCapacity = initialComponentCapacity,
            RequiredModuleIds = GameModuleContext.FoundationModuleIds,
            SimulatedTierCount = ProcessingTierDivisors.SimulatedTierCount,
            CreateContext = services =>
            {
                world = CreateWorld(services, map);
                return new GameModuleContext(services, world, sessionSeed, crawlerNumbers, collectsContentFailures);
            },
            AfterConfigure = CheckConfiguredContent,
            AfterBehavior = (context, ecsContext) => WireBuild(context, ecsContext, world!),
        });

        return new GameModuleBuild(build.EcsContext, world!, build.Context, build.Modules, build.Declarations);
    }

    private static World.World CreateWorld(EcsBuildServices services, Map map)
    {
        var componentManager = services.ComponentManager;
        return new World.World(
            map,
            componentManager.GetMultiPool<NonBlockingComponent>(),
            componentManager.GetMultiPool<ForceBlockingComponent>(),
            services.EventBus,
            services.EntityKeys,
            new TerrainRegistry());
    }

    /// <summary>Resolves the blueprint definitions the modules registered, registers the auras their content names, and checks that content's tags are declared, its toggles can be switched off, its melee is Ground-only and its areas are sound.</summary>
    private static void CheckConfiguredContent(GameModuleContext context)
    {
        // Resolved now, so a broken include fails the load (or a mod's dry run) rather than its first spawn.
        using (EngineHooks.DiagnosticScope("ResolveBlueprints"))
        {
            context.Definitions.ResolveAll();
        }

        AuraContentRegistration.RegisterAll(context);
        ContentTagValidation.EnsureDeclared(context);
        ToggleContentValidation.EnsureValid(context);
        TargetingContentValidation.EnsureValid(context);
        AreaContentValidation.EnsureValid(context);
    }

    /// <summary>The game's own systems and handlers, once every module's are registered.</summary>
    private static void WireBuild(GameModuleContext context, EcsContext ecsContext, World.World world)
    {
        var factory = context.EntityFactory;

        ecsContext.SystemManager.RegisterAtFrameStart(factory.SpawnMoves, GameFrameStartOrder.SpawnMoves);
        SkeletonAccessGuard.Install(ecsContext.ComponentManager, new SkeletonAccessGuard(factory.Skeletons, ecsContext.SystemManager));

        WireEntityDestruction(context, ecsContext, world);
        WireSimulationResume(context);
    }

    /// <summary>Gives diagnostics the skeleton/built split of living entities and the gauges for creature and tier counts and aura storage.</summary>
    private static void RegisterDiagnostics(EcsContext ecsContext, CreatureSkeletons skeletons, LocalTierRoster localTierRoster, ProcessingTierResolver tierResolver, AuraField auraField)
    {
        var entityManager = ecsContext.EntityManager;
        int CountBuiltEntities() => entityManager.LivingEntityCount - skeletons.Count;

        var relationships = ecsContext.ComponentManager.Relationships;
        ecsContext.EntityPopulations = new EntityPopulationPolicy("Built", CountBuiltEntities, componentType => EntityFactory.SkeletonComponentTypes.Contains(componentType) || relationships.IsRelationshipComponentType(componentType));
        ecsContext.Gauges.Register("Entities", "Built", GaugeKind.Level, () => CountBuiltEntities());
        ecsContext.Gauges.Register("Creatures", "Skeletons", GaugeKind.Level, () => skeletons.Count);
        ecsContext.Gauges.Register("ProcessingTier", "Local", GaugeKind.Level, () => localTierRoster.Count);
        ecsContext.Gauges.Register("ProcessingTier", "PendingTransitionNeighborhoods", GaugeKind.Level, () => tierResolver.Transitions.PendingNeighborhoodCount);
        ecsContext.Gauges.Register("Auras", "TotalsChunks", GaugeKind.Level, () => auraField.TotalsChunkCount);
        ecsContext.Gauges.Register("Auras", "TotalsBytes", GaugeKind.Level, () => auraField.TotalsAllocatedBytes);
    }

    /// <summary>Whatever destroys an entity, the state Game keeps about it outside the component pools lets go: its map footprint, its tier bookkeeping and, for a unique, its place in the roster (which also hears of its death).</summary>
    /// <remarks>
    /// Registered after every module's handlers -- the factory's, which forgets a skeleton as it is destroyed, and Auras',
    /// whose source removal reads where the source last was -- so the footprint goes last and each of these sees an
    /// ordinary entity rather than a skeleton the access guard would stop it reading.
    /// </remarks>
    private static void WireEntityDestruction(GameModuleContext context, EcsContext ecsContext, World.World world)
    {
        var transforms = ecsContext.ComponentManager.GetDirectPool<TransformComponent>();
        var processingTierResolver = context.ProcessingTierResolver;
        var uniqueEntities = context.UniqueEntities;

        ecsContext.EventBus.Subscribe<EntityDiedEvent>(died => uniqueEntities.EntityDied(died.EntityId));
        ecsContext.EntityManager.EntityDestroying += entityId =>
        {
            if (transforms.Has(entityId))
            {
                world.RemoveEntityFromMap(entityId, ref transforms.Get(entityId));
            }

            processingTierResolver.Forget(entityId);
            uniqueEntities.EntityDestroying(entityId);
        };
    }

    /// <summary>An entity resumes -- caught up over the span it spent frozen -- whenever a tier change lands it in a simulated tier.</summary>
    /// <remarks>Subscribed after every system, so every stripe set has seen the change before anything resumes.</remarks>
    private static void WireSimulationResume(GameModuleContext context)
    {
        var simulationScope = context.SimulationScope;
        context.ProcessingTierEvents.TierChanged += (entityId, tier) =>
        {
            if (ProcessingTierQuery.IsSimulatedTier(tier))
            {
                simulationScope.RaiseResumed(entityId);
            }
        };
    }
}

/// <summary>What GameBuildPass.BuildModules produced: the ECS, the World over the build's map, the gameModuleContext every module was configured with, the modules and what they declared.</summary>
/// <param name="Modules">This build's own module instances, in the order their phases ran.</param>
public sealed record GameModuleBuild(EcsContext EcsContext, World.World World, GameModuleContext Context, IReadOnlyList<IModule<GameModuleContext>> Modules, ModuleDeclarations Declarations);

/// <summary>Everything one GameBuildPass.Run produced.</summary>
/// <param name="Modules">This build's own module instances, in the order their phases ran.</param>
/// <param name="Declarations">Everything the modules declared, including the setting overrides that were rejected.</param>
/// <param name="LocalTierRoster">The build's Local-tier movers -- see LocalTierRoster.</param>
public sealed record GameBuildPassResult(
    EcsContext EcsContext,
    World.World World,
    GameModuleContext Context,
    IReadOnlyList<IModule<GameModuleContext>> Modules,
    ModuleDeclarations Declarations,
    LocalTierRoster LocalTierRoster)
{
    public EntityFactory Factory => Context.EntityFactory;
}
