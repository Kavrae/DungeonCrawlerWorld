using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Abilities;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, IReadOnlyList<ModuleFailure> Failures, AbilityCatalog AbilityCatalog, FrameEventBuffer<EntityMovedEvent> MovedEntities, ItemCatalog ItemCatalog);