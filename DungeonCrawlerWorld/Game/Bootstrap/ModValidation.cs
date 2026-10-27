using Engine.Bootstrap;
using Engine.Diagnostics;
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
/// sequence, so nothing a mod does during its trial is observable outside it; the build itself refuses
/// a replacement that drops a component the module it replaces registered.
/// </remarks>
public static class ModValidation
{
    public static ValidatedMods Validate(string modsDirectory, IReadOnlyList<ISettingsSource> settingsSources)
    {
        var builtInModules = GameBootstrapper.BuiltInModules();

        ModuleLoadResult<GameModuleContext> loadResult;
        using (EngineHooks.DiagnosticScope("ModuleLoader.LoadFromDirectory"))
        {
            loadResult = ModuleLoader.LoadFromDirectory<GameModuleContext>(modsDirectory);
        }

        var failures = new List<ModuleFailure>(loadResult.Failures);
        var survivors = new List<ModuleFactory<GameModuleContext>>();

        using (EngineHooks.DiagnosticScope("DryRunValidateMods"))
        {
            foreach (var mod in loadResult.ModuleFactories)
            {
                using var trialScope = EngineHooks.DiagnosticScope("Trial", mod.ModuleType.Name);
                try
                {
                    GameBuildPass.BuildModules(builtInModules, [mod], GameBuildPass.CreatePlaceholderMap(), sessionSeed: 0, settingsSources, initialEntityCapacity: 10, initialComponentCapacity: 10, purpose: EcsBuildPurpose.Trial);
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
}

/// <summary>The mods that built, and every load or build failure.</summary>
/// <param name="Mods">A factory for each mod that survived validation, in load order.</param>
public sealed record ValidatedMods(IReadOnlyList<ModuleFactory<GameModuleContext>> Mods, IReadOnlyList<ModuleFailure> Failures)
{
    /// <summary>No mods, and nothing failed.</summary>
    public static ValidatedMods None { get; } = new([], []);
}
