using Engine.Bootstrap;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Class;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.Race;
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
        int initialComponentCapacity)
    {
        IReadOnlyList<IModule> builtInModules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            new MovementModule(),
            new RaceModule(),
            new ClassModule(),
        ];

        var mapQuery = (IMapQuery)world;
        var eventBus = new EventBus();
        var failures = new List<ModuleFailure>();

        var loadResult = ModuleLoader.LoadFromDirectory(modsDirectory);
        failures.AddRange(loadResult.Failures);

        var survivingMods = DryRunValidateMods(builtInModules, loadResult.Modules, mapQuery, mathUtility, failures);

        var modules = ModuleSet.Combine(builtInModules, survivingMods);

        ConfigureGameModules(modules, mapQuery, mathUtility, eventBus);

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity, initialComponentCapacity, eventBus);

        // World is constructed before this method runs (its own doc comment on the World
        // parameter explains why -- MovementModule.Configure needs an IMapQuery before
        // Bootstrapper.Build can produce the ComponentManager these pools come from), so they
        // can't be World constructor dependencies. Wired up here, not left to GameLoop, so
        // every real caller of this method gets them -- absence would silently default every
        // entity to Blocking (see World.IsBlocking).
        world.NonBlockingComponents = ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>();
        world.ForceBlockingComponents = ecsContext.ComponentManager.GetMultiPool<ForceBlockingComponent>();

        // Not held onto -- its EntityMoved subscription (a bound instance-method delegate)
        // keeps it alive for as long as ecsContext.EventBus is.
        _ = new WorldEventSync(world, ecsContext.EventBus);

        return new GameBootstrapResult(ecsContext, failures);
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
        MathUtility mathUtility,
        List<ModuleFailure> failures)
    {
        var survivors = new List<IModule>();

        foreach (var mod in mods)
        {
            try
            {
                var trialModules = new List<IModule>(builtInModules) { mod };
                var throwawayEventBus = new EventBus();

                ConfigureGameModules(trialModules, mapQuery, mathUtility, throwawayEventBus);

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

    private static void ConfigureGameModules(IReadOnlyList<IModule> modules, IMapQuery mapQuery, MathUtility mathUtility, EventBus eventBus)
    {
        var context = new GameModuleContext(mapQuery, mathUtility, eventBus);

        foreach (var module in modules)
        {
            if (module is IGameModule gameModule)
            {
                gameModule.Configure(context);
            }
        }
    }
}
