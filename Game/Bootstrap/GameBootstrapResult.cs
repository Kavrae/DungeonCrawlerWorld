using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, IReadOnlyList<ModuleFailure> Failures, ActionCatalog ActionCatalog, FrameEventBuffer<EntityMovedEvent> MovedEntities, ItemCatalog ItemCatalog);