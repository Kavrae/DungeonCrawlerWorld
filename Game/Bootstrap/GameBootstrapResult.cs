using Engine.ECS.Context;
using Engine.Modules;
using Game.Modules.Abilities;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, IReadOnlyList<ModuleFailure> Failures, AbilityCatalog AbilityCatalog);