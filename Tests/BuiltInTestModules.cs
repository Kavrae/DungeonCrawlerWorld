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
    public static GameBuildPassResult Build(Map map, MathUtility? mathUtility = null, int initialEntityCapacity = 100, int initialComponentCapacity = 50, UniqueNumberAllocator? crawlerNumbers = null) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], map, mathUtility ?? new MathUtility(), [], initialEntityCapacity, initialComponentCapacity, crawlerNumbers: crawlerNumbers);

    /// <summary>Builds modules alone -- the module build without the game wiring a whole set needs -- over map with default settings.</summary>
    /// <remarks>Any of GameModuleContext.FoundationModuleIds modules lacks is added ahead of them: every build needs the pools the context is built from.</remarks>
    public static GameModuleBuild BuildModules(IReadOnlyList<IModule<GameModuleContext>> modules, Map? map = null, MathUtility? mathUtility = null, int initialEntityCapacity = 10, int initialComponentCapacity = 10)
    {
        var missingFoundation = GameBootstrapper.BuiltInModules().CreateAll()
            .Where(builtIn => GameModuleContext.FoundationModuleIds.Contains(builtIn.Id) && modules.All(module => module.Id != builtIn.Id));
        List<IModule<GameModuleContext>> buildModules = [.. missingFoundation, .. modules];

        return GameBuildPass.BuildModules(buildModules, TestModuleRegistration.DefaultSettings(buildModules), map ?? new Map(new Vector3Int(5, 5, 1)), mathUtility ?? new MathUtility(), initialEntityCapacity, initialComponentCapacity);
    }
}
