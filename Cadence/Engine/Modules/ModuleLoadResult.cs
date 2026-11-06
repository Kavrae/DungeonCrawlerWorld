namespace Engine.Modules;

/// <summary>Represents the result of a module loading operation.</summary>
/// <param name="ModuleFactories">A factory for every module type that loaded and constructed.</param>
/// <param name="Failures">The list of module loading failures.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ModuleLoadResult<TContext>(IReadOnlyList<ModuleFactory<TContext>> ModuleFactories, IReadOnlyList<ModuleFailure> Failures);

/// <summary>One mod as ModuleLoader.LoadMods found it: its name, where it came from, its modules and its content, kept or dropped together.</summary>
/// <param name="Source">Its folder, or for a module in a DLL directly in the mods folder that module's type -- what a failure names.</param>
/// <param name="Content">Its Definitions folder as a Mod content layer, or null for a mod with no content.</param>
public sealed record ModPackage<TContext>(string Name, string Source, IReadOnlyList<ModuleFactory<TContext>> Modules, Content.ContentLayer? Content);

/// <summary>Every mod ModuleLoader.LoadMods found, in load order, and every DLL or type that failed to load.</summary>
public sealed record ModLoadResult<TContext>(IReadOnlyList<ModPackage<TContext>> Packages, IReadOnlyList<ModuleFailure> Failures);
