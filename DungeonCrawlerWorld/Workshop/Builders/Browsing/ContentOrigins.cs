using Engine.Content;
using Engine.Modules;
using Game.Content;

namespace Builders.Browsing;

/// <summary>Which mod, if any, supplied each record the session loaded, for a browser row to name it.</summary>
/// <remarks>A record whose winning line is in a mod's layer -- one the mod added, or a master record it replaced -- is the mod's.</remarks>
public sealed class ContentOrigins(GameContent content)
{
    /// <summary>The mod table tableName's record id came from; null for a master record.</summary>
    public string? ModOf(string tableName, Guid id) =>
        content.TryGetLocation(tableName, id, out var location) && location.Layer.Kind == ContentLayerKind.Mod
            ? location.Layer.Name.StartsWith(ModuleLoader.ContentLayerNamePrefix, StringComparison.Ordinal) ? location.Layer.Name[ModuleLoader.ContentLayerNamePrefix.Length..] : location.Layer.Name
            : null;

    /// <summary>name, followed by the mod it came from in brackets when a mod supplied it ("Health Potion [ExampleMod]").</summary>
    public string Label(string tableName, Guid id, string name) =>
        ModOf(tableName, id) is { } mod ? $"{name} [{mod}]" : name;
}
