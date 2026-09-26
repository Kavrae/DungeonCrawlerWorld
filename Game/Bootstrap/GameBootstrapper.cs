using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
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
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.Shops;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

namespace Game.Bootstrap;

/// <summary>
/// Combines the compile-time built-in modules with mods discovered on disk, configures any
/// IGameModule that needs runtime state Engine's Bootstrapper can't supply, and produces the
/// finished EcsContext. This is the actual composition point for "which modules exist" --
/// GameLoop calls this and supplies only the runtime pieces it uniquely owns (World,
/// MathUtility, where to look for mods), never naming a module by type.
/// </summary>
public static class GameBootstrapper
{
    public static GameBootstrapResult Build(
        World.World world,
        MathUtility mathUtility,
        string modsDirectory,
        int initialEntityCapacity,
        int initialComponentCapacity,
        StartupProfiler? startupProfiler = null,
        UniqueNumberAllocator? crawlerNumbers = null,
        ulong runtimeSpawnSeed = 0)
    {
        IReadOnlyList<IModule> builtInModules =
        [
            new Terrain.TerrainModule(),
            new CoreModule(),
            new HealthModule(),
            new ManaModule(),
            // NpcBehaviorModule before MovementModule -- TestCombatBehaviorSystem must run before
            // MovementSystem every frame so a heal/attack decision this tick is visible to
            // MovementSystem's same-frame pending-activation check (see both systems' own doc
            // comments). Component *registration* order doesn't depend on this (every module's
            // RegisterComponents runs before any module's RegisterSystems), only per-frame
            // Update/system-registration order does.
            new NpcBehaviorModule(),
            new MovementModule(),
            new DeathModule(),
            new ProcessingTierModule(),
            new RaceModule(),
            new ClassModule(),
            new BlueprintsModule(),
            new ActionsModule(),
            new CoreActionsModule(),
            new StatusEffectsModule(),
            new StatModifiersModule(),
            new AbilityScoresModule(),
            new BodyPartEffectsModule(),
            new BurningModule(),
            new PoisonModule(),
            new ParalysisModule(),
            new ContactDamageModule(),
            new StatusEffectAuraModule(),
            new AchievementModule(),
            new CrawlerModule(),
            new InventoryModule(),
            new CoreItemsModule(),
            new CurrencyModule(),
            new ContainersModule(),
            new ShopModule(),
        ];

        var mapQuery = (IMapQuery)world;
        var eventBus = new EventBus();
        var failures = new List<ModuleFailure>();

        var entityMoveSync = new WorldEventSync(world);

        ModuleLoadResult loadResult;
        using (startupProfiler?.Phase("ModuleLoader.LoadFromDirectory"))
        {
            loadResult = ModuleLoader.LoadFromDirectory(modsDirectory);
        }
        failures.AddRange(loadResult.Failures);

        List<IModule> survivingMods;
        using (startupProfiler?.Phase("DryRunValidateMods"))
        {
            survivingMods = DryRunValidateMods(builtInModules, loadResult.Modules, mapQuery, world, mathUtility, entityMoveSync, failures);
        }

        var modules = ModuleSet.Combine(builtInModules, survivingMods);

        Engine.ECS.Context.EcsContext rebuilderStaging;
        using (startupProfiler?.Phase("BuildSpawnRecordRebuilderStaging"))
        {
            rebuilderStaging = BuildSpawnRecordRebuilderStaging(modules, mapQuery, world, entityMoveSync);
        }

        var context = ConfigureGameModules(modules, mapQuery, world, mathUtility, eventBus, entityMoveSync, startupProfiler);
        world.Terrain = context.Terrain;

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity, initialComponentCapacity, eventBus, startupProfiler, context.EntityKeys);

        // World is constructed before this method runs (its own doc comment on the World
        // parameter explains why -- MovementModule.Configure needs an IMapQuery before
        // Bootstrapper.Build can produce the ComponentManager these pools come from), so they
        // can't be World constructor dependencies. Wired up here, not left to GameLoop, so
        // every real caller of this method gets them -- absence would silently default every
        // entity to Blocking (see World.IsBlocking).
        world.NonBlockingComponents = ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>();
        world.ForceBlockingComponents = ecsContext.ComponentManager.GetMultiPool<ForceBlockingComponent>();
        world.EntityManager = ecsContext.EntityManager;
        world.EntityKeys = ecsContext.EntityManager.Keys;
        world.EventBus = ecsContext.EventBus;

        // Every placement through World gets a correct tier without its caller having to ask -- the
        // catch-all behind ProcessingTierResolver.CreateEntityAt. See that class's own remarks.
        world.EntityPlaced += context.ProcessingTierResolver.EnsureTiered;

        // One factory for every entity this session ever spawns (see EntityFactory). Skeletons are
        // wired onto it afterwards rather than passed in, since they are built around the same
        // factory -- one of the two has to know about the other second.
        var factory = new EntityFactory(context.Definitions, world, ecsContext.EntityManager, ecsContext.ComponentManager, context.MovedEntities, context.ProcessingTierResolver, context.SimulationClock, crawlerNumbers, runtimeSpawnSeed);

        // Forgotten first, so every later destruction handler sees an ordinary entity rather than a
        // skeleton the access guard would stop it reading.
        var skeletons = new CreatureSkeletons(factory, ecsContext.ComponentManager, context.SimulationClock);
        factory.Skeletons = skeletons;
        ecsContext.EntityManager.EntityDestroying += skeletons.Forget;

        // First in the frame, so a spawn recorded late last frame reaches every reader of the moves.
        ecsContext.SystemManager.RegisterFirst(factory.SpawnMoves!);
        context.EntityFactory = factory;
        SkeletonAccessGuard.Install(ecsContext.ComponentManager, new SkeletonAccessGuard(skeletons, ecsContext.SystemManager));

        WireEntityDestruction(context, ecsContext, world);

        // Which processing tiers are simulated at all: every ITieredSystem through SystemManager, and
        // every timer wheel through SimulationScope. Engine only ever sees a count and a predicate;
        // what the tiers mean stays here.
        ecsContext.SystemManager.SimulatedTierCount = ProcessingTierDivisors.SimulatedTierCount;
        WireSimulationScope(context, ecsContext, skeletons);

        // The clock modules were configured against (and captured) becomes the one SystemManager
        // advances, so every deadline reader sees the same "now". Presentation reaches it as
        // EcsContext.SystemManager.Clock.
        ecsContext.SystemManager.Clock = context.SimulationClock;

        return new GameBootstrapResult(ecsContext, failures, context.Actions, context.MovedEntities, context.Items, context.StatusEffectDisplays, context.LocalTierRoster, context.ProcessingTierResolver, context.Terrain, context.Definitions, new SpawnRecordRebuilder(rebuilderStaging, context.Definitions), skeletons, factory);
    }

    /// <summary>A separately configured and built copy of every module, as the staging world SpawnRecordRebuilder rebuilds creatures in.</summary>
    /// <remarks>
    /// Runs before the real configuration and build, the same way DryRunValidateMods does: modules that
    /// keep what Configure/RegisterSystems hand them end up holding the real world's, since that runs
    /// last. Configured with its own random sequence so staging never draws from the session's. Its
    /// builds read the session's own BlueprintRegistry, so a spawn record means the same
    /// blueprint in both worlds, including one registered after this runs.
    /// </remarks>
    private static Engine.ECS.Context.EcsContext BuildSpawnRecordRebuilderStaging(IReadOnlyList<IModule> modules, IMapQuery mapQuery, IPlayerQuery playerQuery, IEntityMoveSync entityMoveSync)
    {
        var stagingEventBus = new EventBus();
        var stagingContext = ConfigureGameModules(modules, mapQuery, playerQuery, new MathUtility(new SeededRandom()), stagingEventBus, entityMoveSync);
        return Bootstrapper.Build(modules, initialEntityCapacity: 16, initialComponentCapacity: 16, stagingEventBus, entityKeys: stagingContext.EntityKeys);
    }

    /// <summary>Whatever destroys an entity, the state Game keeps about it outside the component pools lets go first: its aura sources, its map footprint, and its tier bookkeeping.</summary>
    /// <remarks>Aura sources go before the footprint: their removal reads where the source last was, and removing the footprint leaves the entity unplaced.</remarks>
    private static void WireEntityDestruction(GameModuleContext context, Engine.ECS.Context.EcsContext ecsContext, World.World world)
    {
        var componentManager = ecsContext.ComponentManager;
        var auraSources = componentManager.IsRegistered<StatusEffectAuraSourceComponent>() ? componentManager.GetMultiPool<StatusEffectAuraSourceComponent>() : null;
        var transforms = componentManager.GetDirectPool<TransformComponent>();
        var processingTierResolver = context.ProcessingTierResolver;
        var eventBus = ecsContext.EventBus;

        ecsContext.EntityManager.EntityDestroying += entityId =>
        {
            if (auraSources?.Has(entityId) == true)
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

    /// <summary>An entity is simulated while its processing tier is below ProcessingTierDivisors.SimulatedTierCount, and resumes -- caught up over the span it spent frozen -- whenever a tier change lands it there.</summary>
    /// <remarks>
    /// An entity with no ProcessingTierComponent counts as simulated. That is the opposite of the
    /// stripe sets' fail-open-to-Beyond, deliberately: a stripe set visiting an untiered entity rarely
    /// costs only staleness, while a timer wheel skipping one would stop its timers outright, and
    /// the entities that go untiered (never placed on the map) are exactly the ones nothing would
    /// ever resume.
    /// </remarks>
    /// <remarks>A skeleton promoted into a simulated tier is built on TierChanging, before any TierChanged handler (tier stripe sets, the Local roster, resume and timer catch-up) sees the change.</remarks>
    private static void WireSimulationScope(GameModuleContext context, Engine.ECS.Context.EcsContext ecsContext, CreatureSkeletons skeletons)
    {
        if (!ecsContext.ComponentManager.IsRegistered<ProcessingTierComponent>())
        {
            return;
        }

        var tiers = ecsContext.ComponentManager.GetDirectPool<ProcessingTierComponent>();
        var simulationScope = context.SimulationScope;

        simulationScope.SetPolicy(new ProcessingTierQuery(tiers).IsSimulated);
        context.ProcessingTierEvents.TierChanging += (entityId, tier) =>
        {
            if (ProcessingTierQuery.IsSimulatedTier(tier))
            {
                skeletons.EnsureBuilt(entityId);
            }
        };
        context.ProcessingTierEvents.TierChanged += (entityId, tier) =>
        {
            if (ProcessingTierQuery.IsSimulatedTier(tier))
            {
                simulationScope.RaiseResumed(entityId);
            }
        };
    }

    /// <summary>
    /// Trial-registers each mod module alongside every built-in (not other mods -- no real
    /// mod ecosystem exists yet to justify solving cross-mod dependency ordering), entirely
    /// against throwaway instances, so a mod depending on a built-in component (the common
    /// case) validates correctly while nothing the mod does during the trial is observable
    /// outside it. A mod that throws is excluded and reported; survivors proceed to the real,
    /// unchanged Bootstrapper.Build later, re-running Configure/RegisterComponents/
    /// RegisterSystems for real.
    /// </summary>
    private static List<IModule> DryRunValidateMods(
        IReadOnlyList<IModule> builtInModules,
        IReadOnlyList<IModule> mods,
        IMapQuery mapQuery,
        IPlayerQuery playerQuery,
        MathUtility mathUtility,
        IEntityMoveSync entityMoveSync,
        List<ModuleFailure> failures)
    {
        var survivors = new List<IModule>();

        foreach (var mod in mods)
        {
            try
            {
                var trialModules = new List<IModule>(builtInModules) { mod };
                var throwawayEventBus = new EventBus();

                ConfigureGameModules(trialModules, mapQuery, playerQuery, mathUtility, throwawayEventBus, entityMoveSync);

                Bootstrapper.Build(trialModules, initialEntityCapacity: 10, initialComponentCapacity: 10, throwawayEventBus);

                survivors.Add(mod);
            }
            catch (Exception exception)
            {
                failures.Add(new ModuleFailure(mod.GetType().FullName ?? mod.GetType().Name, exception));
            }
        }

        return survivors;
    }

    private static GameModuleContext ConfigureGameModules(IReadOnlyList<IModule> modules, IMapQuery mapQuery, IPlayerQuery playerQuery, MathUtility mathUtility, EventBus eventBus, IEntityMoveSync entityMoveSync, StartupProfiler? startupProfiler = null)
    {
        var context = new GameModuleContext(mapQuery, mathUtility, eventBus) { PlayerQuery = playerQuery, EntityMoveSync = entityMoveSync };

        foreach (var module in modules)
        {
            if (module is IGameModule gameModule)
            {
                using var _ = startupProfiler?.Phase($"ConfigureGameModules:{module.Name}");
                gameModule.Configure(context);
            }
        }

        // Resolved now, so a broken include fails the load (or a mod's dry run) rather than its first spawn.
        context.Definitions.ResolveAll();

        return context;
    }
}