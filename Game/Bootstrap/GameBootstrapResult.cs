using Engine.ECS.World;
using Engine.Modules;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, IReadOnlyList<ModuleFailure> Failures);
