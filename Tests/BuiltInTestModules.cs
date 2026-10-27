using Engine.Math;
using Engine.Modules;
using Engine.Utilities;
using Game.Bootstrap;
using Game.Modules;
using Game.World;

namespace Tests;

/// <summary>Builds the game's modules for a test: every built-in as one complete GameBuildPass, or a chosen few alone.</summary>
internal static class BuiltInTestModules
{
    /// <summary>Every built-in module, as a complete, self-contained build over map with no mods and default settings.</summary>
    public static GameBuildPassResult Build(Map map, int sessionSeed = 0, int initialEntityCapacity = 100, int initialComponentCapacity = 50, UniqueNumberAllocator? crawlerNumbers = null) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], map, sessionSeed, [], initialEntityCapacity, initialComponentCapacity, crawlerNumbers: crawlerNumbers);

    /// <summary>Builds modules alone -- the module build without what only a whole game session reads -- over map with default settings.</summary>
    /// <remarks>Any of GameModuleContext.FoundationModuleIds modules lacks is added ahead of them: every build needs the pools the context is built from. Each module is handed to the build through a factory that returns it.</remarks>
    public static GameModuleBuild BuildModules(IReadOnlyList<IModule<GameModuleContext>> modules, Map? map = null, int sessionSeed = 0, int initialEntityCapacity = 10, int initialComponentCapacity = 10)
    {
        var missingFoundation = GameBootstrapper.BuiltInModules().CreateAll()
            .Where(builtIn => GameModuleContext.FoundationModuleIds.Contains(builtIn.Id) && modules.All(module => module.Id != builtIn.Id));
        List<IModule<GameModuleContext>> buildModules = [.. missingFoundation, .. modules];

        return GameBuildPass.BuildModules(Factories(buildModules), [], map ?? new Map(new Vector3Int(5, 5, 1)), sessionSeed, [], initialEntityCapacity, initialComponentCapacity);
    }

    /// <summary>A factory for each module that hands back that same instance.</summary>
    public static IReadOnlyList<ModuleFactory<TContext>> Factories<TContext>(IEnumerable<IModule<TContext>> modules) =>
        modules.Select(module => new ModuleFactory<TContext>(module.GetType(), () => module)).ToList();
}
