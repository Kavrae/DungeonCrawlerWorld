namespace Engine.Modules;

/// <summary>Collects content definitions keyed by Guid.</summary>
/// <remarks>
/// A redirect makes a Guid nothing is registered under resolve to another (a disabled record's replacement), so
/// content and code that still name the old Guid get the new definition. Redirects chain; a cycle resolves to nothing.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public class Catalog<T>(Func<T, Guid> idSelector)
{
    private readonly Dictionary<Guid, T> _definitionsById = [];
    private readonly Dictionary<Guid, Guid> _redirects = [];

    /// <summary>How many definitions are registered.</summary>
    public int Count => _definitionsById.Count;

    /// <summary>Every registered definition, in registration order.</summary>
    /// <remarks>Re-registering an id replaces its definition in place, keeping its position.</remarks>
    public IEnumerable<T> Definitions => _definitionsById.Values;

    /// <summary>Registers a definition in the catalog.</summary>
    /// <param name="definition">The definition to register.</param>
    public void Register(T definition) => _definitionsById[idSelector(definition)] = definition;

    /// <summary>Removes the definition registered under id; false when there is none.</summary>
    /// <remarks>Redirects are left as they are: id and every redirect to it then resolve as if id had never been registered -- to nothing, or onward through a redirect of id's own.</remarks>
    public bool Remove(Guid id) => _definitionsById.Remove(id);

    /// <summary>Makes from resolve to whatever to resolves to, whenever nothing is registered under from.</summary>
    public void AddRedirect(Guid from, Guid to) => _redirects[from] = to;

    /// <summary>Tries to get a definition by its ID, following redirects.</summary>
    /// <param name="id">The ID of the definition to retrieve.</param>
    /// <param name="definition">The retrieved definition, or default if not found.</param>
    /// <returns>True if the definition was found; otherwise, false.</returns>
    public virtual bool TryGet(Guid id, out T definition)
    {
        if (_definitionsById.TryGetValue(id, out definition!))
        {
            return true;
        }

        var current = id;
        for (var hops = 0; hops <= _redirects.Count && _redirects.TryGetValue(current, out var next); hops++)
        {
            if (_definitionsById.TryGetValue(next, out definition!))
            {
                return true;
            }

            current = next;
        }

        definition = default!;
        return false;
    }

    /// <summary>The definition registered under id (or redirected to from it); throws when there is none.</summary>
    public T Get(Guid id) =>
        TryGet(id, out var definition)
            ? definition
            : throw new InvalidOperationException($"No {typeof(T).Name} is registered as {id}.");
}
