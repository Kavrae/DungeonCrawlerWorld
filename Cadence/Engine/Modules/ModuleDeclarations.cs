using Engine.Content;
using Engine.Settings;
using Engine.Tags;

namespace Engine.Modules;

/// <summary>What a module set declared before any pool exists: its settings as resolved, its gameplay tags and its content codecs.</summary>
/// <param name="SettingsFailures">Every setting override that was rejected while resolving Settings.</param>
public sealed record ModuleDeclarations(SettingValues Settings, IReadOnlyList<SettingsFailure> SettingsFailures, GameplayTagRegistry Tags, ContentTypeRegistry ContentTypes)
{
    /// <summary>Runs every module's DeclareSettings, DeclareTags and DeclareContentTypes, in that order and in the order given, and resolves the settings against settingsSources.</summary>
    /// <remarks>EcsBuilder runs this for every build, after validating and sorting the set. Call it directly only for modules that are not being built: one module checked on its own, or a test registering one module's components.</remarks>
    public static ModuleDeclarations Declare(IReadOnlyList<IModule> modules, IReadOnlyList<ISettingsSource> settingsSources)
    {
        var settings = SettingsCatalog.Declare(modules).Resolve(settingsSources);
        return new ModuleDeclarations(settings.Values, settings.Failures, GameplayTagRegistry.Declare(modules), ContentTypeRegistry.Declare(modules));
    }
}
