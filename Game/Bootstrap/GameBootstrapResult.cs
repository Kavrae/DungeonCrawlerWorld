using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Abilities;
using Game.World;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, IReadOnlyList<ModuleFailure> Failures, AbilityCatalog AbilityCatalog, FrameEventBuffer<EntityMoved> MovedEntities);