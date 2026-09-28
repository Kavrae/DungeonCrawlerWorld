using Engine.Modules;

namespace Engine.Settings;

/// <summary>Where one module declares its settings during IModule.DeclareSettings.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SettingsDeclarations
{
    private readonly IModule _declaringModule;
    private readonly Dictionary<(Guid ModuleId, string Name), SettingDefinition> _definitions;

    internal SettingsDeclarations(IModule declaringModule, Dictionary<(Guid ModuleId, string Name), SettingDefinition> definitions)
    {
        _declaringModule = declaringModule;
        _definitions = definitions;
    }

    /// <summary>Declares key with its default and an optional check every value must pass.</summary>
    /// <param name="validate">Returns null for an acceptable value, otherwise why it is rejected.</param>
    /// <exception cref="InvalidOperationException">The module has no Id, key belongs to another module, its name is empty or contains '.', '=' or whitespace, it or a setting with the same "ModuleName.SettingName" (ignoring case) is already declared, or defaultValue fails validate.</exception>
    public void Declare<T>(SettingKey<T> key, T defaultValue, Func<T, string?>? validate = null) where T : IParsable<T>
    {
        var moduleName = _declaringModule.Name;

        if (_declaringModule.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"{moduleName} declares setting '{key.Name}' but has no Id; only a module with an Id can own settings.");
        }

        if (key.ModuleId != _declaringModule.Id)
        {
            throw new InvalidOperationException($"{moduleName} declares setting '{key.Name}', which belongs to module {key.ModuleId}.");
        }

        if (string.IsNullOrEmpty(key.Name) || key.Name.Any(character => character is '.' or '=' || char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException($"{moduleName} declares a setting named '{key.Name}'; a setting name must be non-empty with no '.', '=' or whitespace.");
        }

        var definition = new SettingDefinition<T>(key.ModuleId, moduleName, key.Name, defaultValue, validate);
        if (definition.Validate(defaultValue) is { } rejection)
        {
            throw new InvalidOperationException($"{definition.QualifiedName}'s default {defaultValue} is invalid: {rejection}");
        }

        if (_definitions.Values.Any(existing => string.Equals(existing.QualifiedName, definition.QualifiedName, StringComparison.OrdinalIgnoreCase))
            || !_definitions.TryAdd((key.ModuleId, key.Name), definition))
        {
            throw new InvalidOperationException($"{definition.QualifiedName} is declared more than once.");
        }
    }
}
