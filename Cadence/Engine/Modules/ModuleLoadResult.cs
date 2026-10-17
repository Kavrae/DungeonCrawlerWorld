namespace Engine.Modules;

/// <summary>Represents the result of a module loading operation.</summary>
/// <param name="ModuleFactories">A factory for every module type that loaded and constructed.</param>
/// <param name="Failures">The list of module loading failures.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ModuleLoadResult<TContext>(IReadOnlyList<ModuleFactory<TContext>> ModuleFactories, IReadOnlyList<ModuleFailure> Failures);
