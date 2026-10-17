namespace Engine.Settings;

/// <summary>The frozen value of every setting a module set declared, fixed for the life of one build.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SettingValues
{
    private readonly IReadOnlyDictionary<(Guid ModuleId, string Name), object> _values;

    internal SettingValues(IReadOnlyDictionary<(Guid ModuleId, string Name), object> values) => _values = values;

    /// <summary>The values of a module set that declares no settings.</summary>
    public static SettingValues None { get; } = new(new Dictionary<(Guid ModuleId, string Name), object>());

    /// <exception cref="InvalidOperationException">key was never declared by the module set these values were resolved for, or was declared with another type.</exception>
    public T Get<T>(SettingKey<T> key) where T : IParsable<T>
    {
        if (!_values.TryGetValue((key.ModuleId, key.Name), out var value))
        {
            throw new InvalidOperationException($"Setting '{key.Name}' of module {key.ModuleId} was never declared.");
        }

        if (value is not T typedValue)
        {
            throw new InvalidOperationException($"Setting '{key.Name}' of module {key.ModuleId} is a {value.GetType().Name}, not a {typeof(T).Name}.");
        }

        return typedValue;
    }
}
