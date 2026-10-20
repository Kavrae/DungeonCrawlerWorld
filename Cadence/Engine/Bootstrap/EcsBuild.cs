using Engine.ECS.Context;
using Engine.Modules;

namespace Engine.Bootstrap;

/// <summary>What EcsBuilder.Build produced.</summary>
/// <param name="Modules">This build's own module instances, in the order their phases ran.</param>
/// <param name="Declarations">Everything the modules declared, including the setting overrides that were rejected.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record EcsBuild<TContext>(EcsContext EcsContext, TContext Context, IReadOnlyList<IModule<TContext>> Modules, ModuleDeclarations Declarations);
