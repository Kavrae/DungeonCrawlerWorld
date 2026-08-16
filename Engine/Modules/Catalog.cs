namespace Engine.Modules;

/// <summary>Collects content definitions keyed by Guid.</summary>
/// <cleanupVersion>1</cleanupVersion>
public class Catalog<T>(Func<T, Guid> idSelector)
{
    private readonly Dictionary<Guid, T> _definitionsById = [];

    /// <summary>Registers a definition in the catalog.</summary>
    /// <param name="definition">The definition to register.</param>
    public void Register(T definition) => _definitionsById[idSelector(definition)] = definition;

    /// <summary>Tries to get a definition by its ID.</summary>
    /// <param name="id">The ID of the definition to retrieve.</param>
    /// <param name="definition">The retrieved definition, or default if not found.</param>
    /// <returns>True if the definition was found; otherwise, false.</returns>
    public bool TryGet(Guid id, out T definition) => _definitionsById.TryGetValue(id, out definition!);
}
