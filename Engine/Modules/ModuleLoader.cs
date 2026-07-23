using System.Reflection;
using System.Runtime.Loader;

namespace Engine.Modules;

/// <summary>
/// Discovers and constructs IModule types from .dll files in a directory, for runtime
/// (modding) loading rather than the compile-time list built-in modules use. Every failure
/// (an assembly that won't load, a type that won't construct) is caught and reported via
/// ModuleLoadResult.Failures rather than thrown -- one broken mod DLL must never prevent the
/// rest of the folder from loading, or the game from starting at all.
/// </summary>
public static class ModuleLoader
{
    public static ModuleLoadResult LoadFromDirectory(string modsDirectory)
    {
        var modules = new List<IModule>();
        var failures = new List<ModuleFailure>();

        if (!Directory.Exists(modsDirectory))
        {
            return new ModuleLoadResult(modules, failures);
        }

        foreach (var dllPath in Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            LoadModulesFromAssembly(dllPath, modules, failures);
        }

        return new ModuleLoadResult(modules, failures);
    }

    private static void LoadModulesFromAssembly(string dllPath, List<IModule> modules, List<ModuleFailure> failures)
    {
        Assembly assembly;
        try
        {
            // Collectible: not hot-reload mid-session (the constructed IModule instances keep
            // the assembly rooted for as long as the game world using them is alive), but the
            // option to unload between sessions, and isolates one mod's types from another's.
            var context = new AssemblyLoadContext(name: Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
            assembly = context.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception exception)
        {
            failures.Add(new ModuleFailure(dllPath, exception));
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // Some types in the assembly failed to load (e.g. a missing dependency) -- still
            // process whichever types did load rather than discarding the whole assembly.
            types = [.. exception.Types.Where(type => type is not null).Cast<Type>()];
            failures.Add(new ModuleFailure(dllPath, exception));
        }

        foreach (var type in types)
        {
            if (!IsPublicConcreteModuleType(type))
            {
                continue;
            }

            try
            {
                modules.Add((IModule)Activator.CreateInstance(type)!);
            }
            catch (Exception exception)
            {
                // Covers a type with no public parameterless constructor (Activator throws
                // MissingMethodException) as well as the constructor itself throwing.
                failures.Add(new ModuleFailure($"{dllPath}:{type.FullName}", exception));
            }
        }
    }

    private static bool IsPublicConcreteModuleType(Type type) =>
        typeof(IModule).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false, IsPublic: true };
}