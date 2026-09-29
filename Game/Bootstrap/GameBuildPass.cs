using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Engine.Settings;
using Game.Modules;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Game.Bootstrap;

/// <summary>One complete, self-contained build of the game's modules: fresh module instances, its own World over its own Map, and its own EventBus, entity keys and gameModuleContext.</summary>
/// <remarks>
/// Every build goes through here -- the real session, the SpawnRecordRebuilder staging world, and each
/// mod's dry run -- so nothing one build creates is reachable from another, and none depends on running
/// in a particular order relative to the others.
/// </remarks>
public static class GameBuildPass
{
    /// <summary>A bounded map with one tile per layer, for a build that never places anything: a mod's dry run, or the staging world.</summary>
    public static Map CreatePlaceholderMap() => new(new Vector3Int(1, 1, Enum.GetValues<MapLayer>().Length));

    /// <summary>Builds builtInModules with mods combined into them (a mod replaces the built-in sharing its Id), over map, and wires the result the way a game session needs it.</summary>
    /// <param name="mathUtility">The random sequence this build's modules draw from; a build other than the real session passes its own.</param>
    /// <param name="crawlerNumbers">The numbers a crawler draws when first built; null for a build that never builds one.</param>
    /// <param name="runtimeSpawnSeed">Seeds the factory's own sequence for spawns that name no seed.</param>
    public static GameBuildPassResult Run(
        IReadOnlyList<ModuleFactory<GameModuleContext>> builtInModules,
        IReadOnlyList<ModuleFactory<GameModuleContext>> mods,
        Map map,
        MathUtility mathUtility,
        IReadOnlyList<ISettingsSource> settingsSources,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSpawnSeed = 0)
    {
        var modules = ModuleSet.Combine(builtInModules.CreateAll(), mods.CreateAll());
        var settings = SettingsCatalog.Declare(modules).Resolve(settingsSources);

        var build = BuildModules(modules, settings.Values, map, mathUtility, initialEntityCapacity, initialComponentCapacity, crawlerNumbers, runtimeSpawnSeed);
        var ecsContext = build.EcsContext;
        var context = build.Context;
        var componentManager = ecsContext.ComponentManager;
        var factory = context.EntityFactory;

        // First in the frame, so a spawn recorded late last frame reaches every reader of the moves.
        ecsContext.SystemManager.RegisterFirst(factory.SpawnMoves);
        SkeletonAccessGuard.Install(componentManager, new SkeletonAccessGuard(factory.Skeletons, ecsContext.SystemManager));

        WireEntityDestruction(context, ecsContext, build.World);

        // Which processing tiers are simulated at all: every ITieredSystem through SystemManager, and
        // every timer wheel through SimulationScope. Engine only ever sees a count and a predicate;
        // what the tiers mean stays here.
        ecsContext.SystemManager.SimulatedTierCount = ProcessingTierDivisors.SimulatedTierCount;
        WireSimulationResume(context);

        // The clock modules were configured against becomes the one SystemManager advances, so every
        // deadline reader sees the same "now". Presentation reaches it as EcsContext.SystemManager.Clock.
        ecsContext.SystemManager.Clock = context.SimulationClock;

        var localTierRoster = new LocalTierRoster(componentManager.GetPackedPool<MovementComponent>(), componentManager.GetDirectPool<ProcessingTierComponent>(), context.ProcessingTierEvents);

        return new GameBuildPassResult(ecsContext, build.World, context, modules, settings, localTierRoster);
    }

    /// <summary>Runs every phase of modules, and only those, over map: registers their registeredComponents, builds the World and the gameModuleContext from them, configures the modules, resolves the blueprint definitions they registered, and registers their systems.</summary>
    /// <remarks>The module build alone, with none of Run's game wiring -- for a module set that isn't a whole game (a test of a few modules). It must still contain every one of GameModuleContext.FoundationModuleIds.</remarks>
    /// <exception cref="InvalidOperationException">modules lacks a foundation module, or fails EcsBuilder's own checks.</exception>
    public static GameModuleBuild BuildModules(
        IReadOnlyList<IModule<GameModuleContext>> modules,
        SettingValues settings,
        Map map,
        MathUtility mathUtility,
        int initialEntityCapacity,
        int initialComponentCapacity,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSpawnSeed = 0)
    {
        var sortedModules = EcsBuilder.Begin(modules, settings, initialEntityCapacity, initialComponentCapacity, new EventBus());
        var registeredComponents = sortedModules.RegisterComponents();

        ThrowIfMissingFoundation(registeredComponents.Modules);

        var componentManager = registeredComponents.ComponentManager;
        var world = new World.World(
            map,
            componentManager.GetMultiPool<NonBlockingComponent>(),
            componentManager.GetMultiPool<ForceBlockingComponent>(),
            registeredComponents.EventBus,
            registeredComponents.EntityManager.Keys,
            new TerrainRegistry());
        var gameModuleContext = new GameModuleContext(world, componentManager, registeredComponents.EntityManager, registeredComponents.EventBus, settings, mathUtility, crawlerNumbers, runtimeSpawnSeed);

        var configuredModules = registeredComponents.Configure(gameModuleContext);

        // Resolved now, so a broken include fails the load (or a mod's dry run) rather than its first spawn.
        using (EngineHooks.DiagnosticScope("ResolveBlueprints"))
        {
            gameModuleContext.Definitions.ResolveAll();
        }

        var registeredSystems = configuredModules.RegisterSystems();
        var ecsContext = registeredSystems.Complete();

        return new GameModuleBuild(ecsContext, world, gameModuleContext);
    }

    private static void ThrowIfMissingFoundation(IReadOnlyList<IModule<GameModuleContext>> modules)
    {
        var missingIds = GameModuleContext.FoundationModuleIds.Where(foundationId => modules.All(module => module.Id != foundationId)).ToList();
        if (missingIds.Count > 0)
        {
            throw new InvalidOperationException($"Every build needs the modules the GameModuleContext is built from; missing {string.Join(", ", missingIds)}.");
        }
    }

    /// <summary>Whatever destroys an entity, the state Game keeps about it outside the component pools lets go first: its aura sources, its map footprint, and its tier bookkeeping.</summary>
    /// <remarks>
    /// Aura sources go before the footprint: their removal reads where the source last was, and removing the
    /// footprint leaves the entity unplaced. Registered after the factory (which forgets a skeleton as it is
    /// destroyed), so each of these sees an ordinary entity rather than a skeleton the access guard would stop
    /// it reading.
    /// </remarks>
    private static void WireEntityDestruction(GameModuleContext context, EcsContext ecsContext, World.World world)
    {
        var componentManager = ecsContext.ComponentManager;
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var transforms = componentManager.GetDirectPool<TransformComponent>();
        var processingTierResolver = context.ProcessingTierResolver;
        var eventBus = ecsContext.EventBus;

        ecsContext.EntityManager.EntityDestroying += entityId =>
        {
            if (auraSources.Has(entityId))
            {
                AuraSourceEffects.RemoveAll(auraSources, eventBus, entityId);
            }

            if (transforms.Has(entityId))
            {
                world.RemoveEntityFromMap(entityId, ref transforms.Get(entityId));
            }

            processingTierResolver.Forget(entityId);
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

/// <summary>What GameBuildPass.BuildModules produced: the ECS, the World over the build's map, and the gameModuleContext every module was configured with.</summary>
public sealed record GameModuleBuild(EcsContext EcsContext, World.World World, GameModuleContext Context);

/// <summary>Everything one GameBuildPass produced.</summary>
/// <param name="Modules">This build's own module instances, in the order they were combined.</param>
/// <param name="LocalTierRoster">The build's Local-tier movers -- see LocalTierRoster.</param>
public sealed record GameBuildPassResult(
    EcsContext EcsContext,
    World.World World,
    GameModuleContext Context,
    IReadOnlyList<IModule<GameModuleContext>> Modules,
    SettingsResolution Settings,
    LocalTierRoster LocalTierRoster)
{
    public EntityFactory Factory => Context.EntityFactory;
}
