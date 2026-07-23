namespace Engine.Modules;

/// <summary>Combines built-in modules with mod-loaded ones, replacing by Id.</summary>
public static class ModuleSet
{
    /// <summary>
    /// A mod module whose Id matches a module already in the combined list (built-in, or an
    /// earlier mod -- load order determines final precedence) replaces it in place; anything
    /// else is added alongside. Guid.Empty (IModule's default, unset Id) never matches
    /// anything, even another unset Guid.Empty -- otherwise two mods that both forgot to set
    /// an Id would incorrectly replace each other instead of coexisting.
    /// </summary>
    public static IReadOnlyList<IModule> Combine(IReadOnlyList<IModule> builtIn, IReadOnlyList<IModule> mods)
    {
        var combined = new List<IModule>(builtIn);

        foreach (var mod in mods)
        {
            var replaceIndex = mod.Id != Guid.Empty
                ? combined.FindIndex(m => m.Id == mod.Id)
                : -1;

            if (replaceIndex >= 0)
            {
                combined[replaceIndex] = mod;
            }
            else
            {
                combined.Add(mod);
            }
        }

        return combined;
    }
}