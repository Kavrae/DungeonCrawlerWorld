using Engine.Modules;

namespace Engine.Settings;

/// <summary>Every setting a module set declares, and the values those settings take once overrides are applied.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SettingsCatalog
{
    private readonly Dictionary<(Guid ModuleId, string Name), SettingDefinition> _definitionsByKey;

    private SettingsCatalog(Dictionary<(Guid ModuleId, string Name), SettingDefinition> definitionsByKey) => _definitionsByKey = definitionsByKey;

    public IReadOnlyCollection<SettingDefinition> Definitions => _definitionsByKey.Values;

    /// <summary>Runs every module's DeclareSettings, in order.</summary>
    /// <exception cref="InvalidOperationException">A declaration is invalid -- see SettingsDeclarations.Declare.</exception>
    public static SettingsCatalog Declare(IEnumerable<IModule> modules)
    {
        var definitionsByKey = new Dictionary<(Guid ModuleId, string Name), SettingDefinition>();

        foreach (var module in modules)
        {
            module.DeclareSettings(new SettingsDeclarations(module, definitionsByKey));
        }

        return new SettingsCatalog(definitionsByKey);
    }

    /// <summary>Every setting's default, replaced by each source's overrides in order.</summary>
    /// <remarks>
    /// An override naming no declared setting, one whose value doesn't parse or validate, and an entry a
    /// source couldn't read are each reported as a failure and change nothing. Setting names match
    /// case-insensitively.
    /// </remarks>
    public SettingsResolution Resolve(IEnumerable<ISettingsSource> sources)
    {
        var values = _definitionsByKey.ToDictionary(entry => entry.Key, entry => entry.Value.DefaultValue);
        var definitionsByQualifiedName = _definitionsByKey.Values.ToDictionary(definition => definition.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var failures = new List<SettingsFailure>();

        foreach (var source in sources)
        {
            foreach (var malformedEntry in source.MalformedEntries)
            {
                failures.Add(new SettingsFailure(source.Name, malformedEntry, "Not a setting override; expected ModuleName.SettingName=Value."));
            }

            foreach (var settingOverride in source.Overrides)
            {
                var qualifiedName = $"{settingOverride.ModuleName}.{settingOverride.SettingName}";

                if (!definitionsByQualifiedName.TryGetValue(qualifiedName, out var definition))
                {
                    failures.Add(new SettingsFailure(source.Name, qualifiedName, "No module in this build declares this setting."));
                    continue;
                }

                if (definition.TryParse(settingOverride.Value, out var value) is { } rejection)
                {
                    failures.Add(new SettingsFailure(source.Name, qualifiedName, rejection));
                    continue;
                }

                values[(definition.ModuleId, definition.Name)] = value!;
            }
        }

        return new SettingsResolution(new SettingValues(values), failures);
    }
}

/// <summary>The resolved values, and every override that was rejected along the way.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record SettingsResolution(SettingValues Values, IReadOnlyList<SettingsFailure> Failures);

/// <summary>An override that was rejected, where it came from, and why.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct SettingsFailure(string Source, string Setting, string Message);
