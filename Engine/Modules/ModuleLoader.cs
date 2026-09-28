using System.Reflection;
using System.Runtime.Loader;

namespace Engine.Modules;

/// <summary>Discovers IModule&lt;TContext&gt; types in .dll files in a directory, returning a factory for each.</summary>
/// <remarks>
/// For runtime (modding) loading rather than the compile-time list built-in modules use. Every failure
/// (an assembly that won't load, a type that won't construct, a module built for another context) is
/// caught and reported via ModuleLoadResult.Failures rather than thrown -- one broken mod DLL must never
/// prevent the rest of the folder from loading, or the game from starting at all.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ModuleLoader
{
    /// <summary>A factory for every public concrete IModule&lt;TContext&gt; in the DLLs in modsDirectory.</summary>
    /// <remarks>Each type is constructed once here, so a type that won't construct is reported now rather than failing a build later.</remarks>
    /// <param name="modsDirectory">The directory containing the module DLLs.</param>
    public static ModuleLoadResult<TContext> LoadFromDirectory<TContext>(string modsDirectory)
    {
        var moduleFactories = new List<ModuleFactory<TContext>>();
        var failures = new List<ModuleFailure>();

        if (!Directory.Exists(modsDirectory))
        {
            return new ModuleLoadResult<TContext>(moduleFactories, failures);
        }

        foreach (var dllPath in Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            LoadModulesFromAssembly(dllPath, moduleFactories, failures);
        }

        return new ModuleLoadResult<TContext>(moduleFactories, failures);
    }

    private static void LoadModulesFromAssembly<TContext>(string dllPath, List<ModuleFactory<TContext>> moduleFactories, List<ModuleFailure> failures)
    {
        Assembly assembly;
        try
        {
            // Collectible: not hot-reload mid-session (the factories and the module instances they
            // create keep the assembly rooted for as long as anything holds them), but the option to
            // unload between sessions, and isolates one mod's types from another's.
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

            if (!typeof(IModule<TContext>).IsAssignableFrom(type))
            {
                failures.Add(new ModuleFailure($"{dllPath}:{type.FullName}", new InvalidOperationException($"{type.Name} is a module, but not an {nameof(IModule)}<{typeof(TContext).Name}>.")));
                continue;
            }

            try
            {
                Activator.CreateInstance(type);
                moduleFactories.Add(new ModuleFactory<TContext>(type, () => (IModule<TContext>)Activator.CreateInstance(type)!));
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
