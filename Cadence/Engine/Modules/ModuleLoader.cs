using System.Reflection;
using System.Runtime.Loader;

namespace Engine.Modules;

/// <summary>Discovers IModule&lt;TContext&gt; types in .dll files in a directory, returning a factory for each.</summary>
/// <remarks>
/// For runtime (modding) loading rather than the compile-time list built-in modules use. Every failure
/// (an assembly that won't load, a type that won't construct, a module built for another context) is
/// caught and reported via ModuleLoadResult.Failures rather than thrown -- one broken mod DLL must never
/// prevent the rest of the folder from loading, or the game from starting at all.
///
/// DLLs load in ordinal file-name order, so when two mods replace the same module the same one wins on
/// every machine. Each DLL gets its own collectible load context (ModAssemblyLoadContext), which shares
/// the host's assemblies and resolves the mod's own dependencies beside it. A DLL with no modules (a
/// mod's dependency) loads and contributes nothing.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public static class ModuleLoader
{
    /// <summary>A factory for every public concrete IModule&lt;TContext&gt; in the DLLs in modsDirectory, in file-name order.</summary>
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

        var dllPaths = Directory.GetFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        Array.Sort(dllPaths, StringComparer.Ordinal);

        foreach (var dllPath in dllPaths)
        {
            LoadModulesFromAssembly(dllPath, moduleFactories, failures);
        }

        return new ModuleLoadResult<TContext>(moduleFactories, failures);
    }

    /// <summary>The folder inside a mod's folder that holds its content tables, a Mod content layer.</summary>
    public const string DefinitionsDirectoryName = "Definitions";

    /// <summary>What a mod's content layer is named before the mod's own name ("Mod ExampleMod").</summary>
    public const string ContentLayerNamePrefix = "Mod ";

    /// <summary>Every mod in modsDirectory, in name order: each subfolder is one mod -- the modules of every DLL directly in it, and its Definitions folder as a content layer named "Mod " and the folder's name -- and each module in a DLL directly in modsDirectory is a mod of its own with no content.</summary>
    /// <remarks>
    /// A mod's modules and content are kept and dropped together, so content that needs its own mod's codecs is never
    /// read without them: a folder in which any DLL or module fails to load or construct is left out whole, its failures
    /// reported as LoadFromDirectory reports them and one more naming the mod. A folder with neither a DLL nor a
    /// Definitions folder isn't a mod.
    /// </remarks>
    public static ModLoadResult<TContext> LoadMods<TContext>(string modsDirectory)
    {
        var packages = new List<ModPackage<TContext>>();
        var failures = new List<ModuleFailure>();
        if (!Directory.Exists(modsDirectory))
        {
            return new ModLoadResult<TContext>(packages, failures);
        }

        var looseModules = LoadFromDirectory<TContext>(modsDirectory);
        failures.AddRange(looseModules.Failures);
        packages.AddRange(looseModules.ModuleFactories.Select(factory => new ModPackage<TContext>(factory.ModuleType.Name, factory.ModuleType.FullName ?? factory.ModuleType.Name, [factory], Content: null)));

        var folders = Directory.GetDirectories(modsDirectory);
        Array.Sort(folders, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            var modules = LoadFromDirectory<TContext>(folder);
            failures.AddRange(modules.Failures);
            if (modules.Failures.Count > 0)
            {
                failures.Add(new ModuleFailure(folder, new InvalidOperationException($"Mod {name} is left out, its modules and content together: {modules.Failures.Count} of its DLLs or modules failed to load.")));
                continue;
            }

            var definitions = Path.Combine(folder, DefinitionsDirectoryName);
            var content = Directory.Exists(definitions) ? Content.ContentLayer.FromDirectory(ContentLayerNamePrefix + name, Content.ContentLayerKind.Mod, definitions) : null;
            if (modules.ModuleFactories.Count > 0 || content is not null)
            {
                packages.Add(new ModPackage<TContext>(name, folder, modules.ModuleFactories, content));
            }
        }

        return new ModLoadResult<TContext>(packages, failures);
    }

    private static void LoadModulesFromAssembly<TContext>(string dllPath, List<ModuleFactory<TContext>> moduleFactories, List<ModuleFailure> failures)
    {
        Assembly assembly;
        try
        {
            assembly = new ModAssemblyLoadContext(dllPath).LoadFromAssemblyPath(dllPath);
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
                failures.Add(new ModuleFailure($"{dllPath}:{type.FullName}", exception));
            }
        }
    }

    private static bool IsPublicConcreteModuleType(Type type) =>
        typeof(IModule).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false, IsPublic: true };

    /// <summary>One mod DLL's load context: the host's assemblies come from the default context, anything else from the mod's deps.json or its folder.</summary>
    /// <remarks>
    /// Collectible: not hot-reload mid-session (the factories and module instances keep the assembly rooted for as long as
    /// anything holds them), but the option to unload between sessions, and isolation of one mod's types from another's.
    /// An assembly the host already has always resolves to the host's copy, so a mod that ships its own Engine.dll still
    /// shares the host's types.
    /// </remarks>
    private sealed class ModAssemblyLoadContext(string modDllPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(modDllPath), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _dependencyResolver = new(modDllPath);
        private readonly string _modDirectory = Path.GetDirectoryName(modDllPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (IsHostAssembly(assemblyName))
            {
                return null;
            }

            if (_dependencyResolver.ResolveAssemblyToPath(assemblyName) is { } resolvedPath)
            {
                return LoadFromAssemblyPath(resolvedPath);
            }

            var besideModPath = Path.Combine(_modDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(besideModPath) ? LoadFromAssemblyPath(besideModPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } resolvedPath ? LoadUnmanagedDllFromPath(resolvedPath) : IntPtr.Zero;

        private static bool IsHostAssembly(AssemblyName assemblyName)
        {
            foreach (var hostAssembly in Default.Assemblies)
            {
                if (AssemblyName.ReferenceMatchesDefinition(assemblyName, hostAssembly.GetName()))
                {
                    return true;
                }
            }

            return File.Exists(Path.Combine(AppContext.BaseDirectory, $"{assemblyName.Name}.dll"));
        }
    }
}
