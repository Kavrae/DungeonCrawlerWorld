using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.Math;
using Engine.Modules;
using Engine.Settings;
using Game.Modules;

namespace Game.Bootstrap;

/// <summary>Loads the mods in a directory and keeps the ones that build: each is trial-built with every built-in, in a complete build of its own.</summary>
/// <remarks>
/// Separate from GameBootstrapper.Build so the two can run at different times: validation when the game
/// starts or its mods change, the real build when a game is started or loaded. A mod is combined only with
/// the built-ins, the way the real build combines them so a replacement stands in for the module it
/// replaces -- not with other mods, since no mod ecosystem exists yet to justify solving cross-mod
/// dependency ordering. Each trial is a GameBuildPass over a placeholder map with its own random
/// sequence, so nothing a mod does during its trial is observable outside it.
/// </remarks>
public static class ModValidation
{
    public static ValidatedMods Validate(string modsDirectory, IReadOnlyList<ISettingsSource> settingsSources, StartupProfiler? startupProfiler = null)
    {
        var builtInModules = GameBootstrapper.BuiltInModules();

        ModuleLoadResult<GameModuleContext> loadResult;
        using (startupProfiler?.Phase("ModuleLoader.LoadFromDirectory"))
        {
            loadResult = ModuleLoader.LoadFromDirectory<GameModuleContext>(modsDirectory);
        }

        var failures = new List<ModuleFailure>(loadResult.Failures);
        var survivors = new List<ModuleFactory<GameModuleContext>>();

        using (startupProfiler?.Phase("DryRunValidateMods"))
        {
            foreach (var mod in loadResult.ModuleFactories)
            {
                try
                {
                    ThrowIfBreaksReplacementContract(builtInModules, mod, settingsSources);
                    GameBuildPass.Run(builtInModules, [mod], GameBuildPass.CreatePlaceholderMap(), new MathUtility(new SeededRandom()), settingsSources, initialEntityCapacity: 10, initialComponentCapacity: 10);
                    survivors.Add(mod);
                }
                catch (Exception exception)
                {
                    failures.Add(new ModuleFailure(mod.ModuleType.FullName ?? mod.ModuleType.Name, exception));
                }
            }
        }

        return new ValidatedMods(survivors, failures);
    }

    /// <summary>Throws unless a mod that replaces a built-in registers every component the built-in did, each as the same kind of pool.</summary>
    private static void ThrowIfBreaksReplacementContract(IReadOnlyList<ModuleFactory<GameModuleContext>> builtInModules, ModuleFactory<GameModuleContext> modFactory, IReadOnlyList<ISettingsSource> settingsSources)
    {
        var mod = modFactory.Create();
        if (mod.Id == Guid.Empty || builtInModules.CreateAll().FirstOrDefault(builtIn => builtIn.Id == mod.Id) is not { } replacedModule)
        {
            return;
        }

        var providedPools = RegisteredPools(mod, settingsSources);
        var missingPools = RegisteredPools(replacedModule, settingsSources)
            .Where(requiredPool => !providedPools.Contains(requiredPool))
            .Select(missingPool => $"{missingPool.ComponentType.Name} ({missingPool.PoolKind.Name[..missingPool.PoolKind.Name.IndexOf('`')]})")
            .ToList();

        if (missingPools.Count > 0)
        {
            throw new InvalidOperationException($"{mod.Name} replaces {replacedModule.Name} but does not register: {string.Join(", ", missingPools)}.");
        }
    }

    /// <summary>Each component type a module registers, with the kind of pool it registers it as (DirectComponentPool&lt;&gt; and so on).</summary>
    /// <remarks>Registered with the module's own settings as the real build would resolve them, so a setting that sizes or tunes a pool is compared as the player configured it.</remarks>
    private static HashSet<(Type ComponentType, Type PoolKind)> RegisteredPools(IModule module, IReadOnlyList<ISettingsSource> settingsSources)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 1, initialComponentCapacity: 1);
        var settings = SettingsCatalog.Declare([module]).Resolve(settingsSources).Values;
        module.RegisterComponents(new ComponentRegistration(componentManager, settings));

        return componentManager.AllPools.Select(pool => (pool.ComponentType, pool.GetType().GetGenericTypeDefinition())).ToHashSet();
    }
}

/// <summary>The mods that built, and every load or build failure.</summary>
/// <param name="Mods">A factory for each mod that survived validation, in load order.</param>
public sealed record ValidatedMods(IReadOnlyList<ModuleFactory<GameModuleContext>> Mods, IReadOnlyList<ModuleFailure> Failures)
{
    /// <summary>No mods, and nothing failed.</summary>
    public static ValidatedMods None { get; } = new([], []);
}
