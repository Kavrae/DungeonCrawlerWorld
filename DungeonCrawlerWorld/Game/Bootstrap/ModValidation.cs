using Engine.Bootstrap;
using Engine.Content;
using Engine.Diagnostics;
using Engine.Modules;
using Engine.Settings;
using Game.Modules;

namespace Game.Bootstrap;

/// <summary>Loads the mods in a directory and keeps the ones that build: each -- its modules and its content together -- is trial-built with every built-in, in a complete build of its own.</summary>
/// <remarks>
/// Separate from GameBootstrapper.Build so the two can run at different times: validation when the game
/// starts or its mods change, the real build when a game is started or loaded. A mod is combined only with
/// the built-ins, the way the real build combines them so a replacement stands in for the module it
/// replaces -- not with other mods, since no mod ecosystem exists yet to justify solving cross-mod
/// dependency ordering. Each trial is a GameBuildPass over a placeholder map with its own random
/// sequence, so nothing a mod does during its trial is observable outside it; the build itself refuses
/// a replacement that drops a component the module it replaces registered.
///
/// A mod's content (its Definitions folder, ModuleLoader.LoadMods) is read whole in its trial, over the content given
/// and nothing of any other mod's, so a record that fails to load or check fails the trial and drops the whole mod, its
/// modules with it; the failure names the mod's layer, file and line. The survivors' layers go on the stack in load order
/// (ValidatedMods.ContentLayers).
///
/// First of all, the built-ins alone are trial-built over every record of the content: a game loads its actions and items
/// as they are needed, so this is where a broken master record fails before play (it throws -- there is no game without
/// the master). Each trial reads every record, as every build that isn't a game's session does.
/// </remarks>
public static class ModValidation
{
    /// <param name="content">The content layers every trial build reads -- the same the session will.</param>
    /// <param name="collectsContentFailures">For a tool (the IDE): each trial leaves out content that fails rather than throwing, so a broken record drops no mod, and the built-ins' own trial is skipped -- the tool's build lists what is wrong instead.</param>
    /// <param name="modsDirectory">The folder of mods to load (PlayerFolders.Mods), or null to load none -- the built-ins are still trial-built.</param>
    public static ValidatedMods Validate(string? modsDirectory, ContentLayers content, IReadOnlyList<ISettingsSource> settingsSources, bool collectsContentFailures = false)
    {
        var builtInModules = GameBootstrapper.BuiltInModules();

        ModLoadResult<GameModuleContext> loadResult;
        using (EngineHooks.DiagnosticScope("ModuleLoader.LoadMods"))
        {
            loadResult = modsDirectory is null ? new ModLoadResult<GameModuleContext>([], []) : ModuleLoader.LoadMods<GameModuleContext>(modsDirectory);
        }

        var failures = new List<ModuleFailure>(loadResult.Failures);
        var survivors = new List<ModuleFactory<GameModuleContext>>();
        var survivingLayers = new List<ContentLayer>();

        using (EngineHooks.DiagnosticScope("DryRunValidateMods"))
        {
            if (!collectsContentFailures)
            {
                using var builtInTrialScope = EngineHooks.DiagnosticScope("Trial", "Built-in content");
                GameBuildPass.BuildModules(builtInModules, [], GameBuildPass.CreatePlaceholderMap(), content, sessionSeed: 0, settingsSources, initialEntityCapacity: 10, initialComponentCapacity: 10, purpose: EcsBuildPurpose.Trial);
            }

            foreach (var mod in loadResult.Packages)
            {
                using var trialScope = EngineHooks.DiagnosticScope("Trial", mod.Name);
                var trialContent = mod.Content is { } modContent ? content.With(modContent) : content;
                try
                {
                    GameBuildPass.BuildModules(builtInModules, mod.Modules, GameBuildPass.CreatePlaceholderMap(), trialContent, sessionSeed: 0, settingsSources, initialEntityCapacity: 10, initialComponentCapacity: 10, purpose: EcsBuildPurpose.Trial, collectsContentFailures: collectsContentFailures);
                    survivors.AddRange(mod.Modules);
                    if (mod.Content is { } survivingContent)
                    {
                        survivingLayers.Add(survivingContent);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(new ModuleFailure(mod.Source, exception));
                }
            }
        }

        return new ValidatedMods(survivors, failures, survivingLayers);
    }
}

/// <summary>The mods that built, and every load or build failure.</summary>
/// <param name="Mods">A factory for each module of a mod that survived validation, in load order.</param>
/// <param name="ContentLayers">The content of each mod that survived validation, in load order: the Mod layers a session reads between master and its save.</param>
public sealed record ValidatedMods(IReadOnlyList<ModuleFactory<GameModuleContext>> Mods, IReadOnlyList<ModuleFailure> Failures, IReadOnlyList<ContentLayer> ContentLayers)
{
    /// <summary>No mods, and nothing failed.</summary>
    public static ValidatedMods None { get; } = new([], [], []);
}
