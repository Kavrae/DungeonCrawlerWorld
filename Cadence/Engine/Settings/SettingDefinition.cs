using System.Globalization;

namespace Engine.Settings;

/// <summary>One declared setting: who owns it, its type, its default, and how an override is parsed and checked.</summary>
/// <cleanupVersion>1</cleanupVersion>
public abstract class SettingDefinition(Guid moduleId, string moduleName, string name, object defaultValue)
{
    public Guid ModuleId { get; } = moduleId;

    /// <summary>The declaring module's Name, which an override names the setting by.</summary>
    public string ModuleName { get; } = moduleName;

    public string Name { get; } = name;

    public object DefaultValue { get; } = defaultValue;

    public abstract Type ValueType { get; }

    /// <summary>The name an override uses: "ModuleName.SettingName".</summary>
    public string QualifiedName => $"{ModuleName}.{Name}";

    /// <summary>Parses text as this setting's type with the invariant culture, then validates it.</summary>
    /// <returns>Null on success, otherwise why the text was rejected.</returns>
    public abstract string? TryParse(string text, out object? value);
}

/// <summary>A declared setting of type T.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SettingDefinition<T>(Guid moduleId, string moduleName, string name, T defaultValue, Func<T, string?>? validate)
    : SettingDefinition(moduleId, moduleName, name, defaultValue) where T : IParsable<T>
{
    public override Type ValueType => typeof(T);

    /// <summary>Null when value is acceptable, otherwise why it isn't.</summary>
    public string? Validate(T value) => validate?.Invoke(value);

    public override string? TryParse(string text, out object? value)
    {
        value = null;
        if (!T.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"'{text}' is not a valid {typeof(T).Name}.";
        }

        if (Validate(parsed) is { } rejection)
        {
            return rejection;
        }

        value = parsed;
        return null;
    }
}
