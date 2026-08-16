using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.Modules.Burning;
using Game.Modules.Class;
using Game.Modules.ContactDamage;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Crawler;
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
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffects;
using Game.World;

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
        StartupProfiler? startupProfiler = null)
    {
        IReadOnlyList<IModule> builtInModules =
        [
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
            new ActionsModule(),
            new CoreActionsModule(),
            new StatusEffectsModule(),
            new StatModifiersModule(),
            new AbilityScoresModule(),
            new BurningModule(),
            new PoisonModule(),
            new ParalysisModule(),
            new ContactDamageModule(),
            new StatusEffectAuraModule(),
            new AchievementModule(),
            new CrawlerModule(),
            new InventoryModule(),
            new CoreItemsModule(),
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

        var context = ConfigureGameModules(modules, mapQuery, world, mathUtility, eventBus, entityMoveSync, startupProfiler);

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity, initialComponentCapacity, eventBus, startupProfiler);

        // World is constructed before this method runs (its own doc comment on the World
        // parameter explains why -- MovementModule.Configure needs an IMapQuery before
        // Bootstrapper.Build can produce the ComponentManager these pools come from), so they
        // can't be World constructor dependencies. Wired up here, not left to GameLoop, so
        // every real caller of this method gets them -- absence would silently default every
        // entity to Blocking (see World.IsBlocking).
        world.NonBlockingComponents = ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>();
        world.ForceBlockingComponents = ecsContext.ComponentManager.GetMultiPool<ForceBlockingComponent>();
        world.EntityManager = ecsContext.EntityManager;

        return new GameBootstrapResult(ecsContext, failures, context.Actions, context.MovedEntities, context.Items);
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

        return context;
    }
}