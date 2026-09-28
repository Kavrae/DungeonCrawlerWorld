namespace Engine.Settings;

/// <summary>Names one setting: the Id of the module that owns it, and its name within that module.</summary>
/// <remarks>A module declares its keys as static fields, so any code can read the value through <see cref="SettingValues.Get{T}"/>. A module replacing another by Id owns the replaced module's keys.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record SettingKey<T>(Guid ModuleId, string Name) where T : IParsable<T>;
