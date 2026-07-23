namespace Engine.Modules;

public sealed record ModuleLoadResult(IReadOnlyList<IModule> Modules, IReadOnlyList<ModuleFailure> Failures);