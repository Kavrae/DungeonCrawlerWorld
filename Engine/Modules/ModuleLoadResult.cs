namespace Engine.Modules;

/// <summary>Represents the result of a module loading operation.</summary>
/// <param name="Modules">The list of successfully loaded modules.</param>
/// <param name="Failures">The list of module loading failures.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ModuleLoadResult(IReadOnlyList<IModule> Modules, IReadOnlyList<ModuleFailure> Failures);