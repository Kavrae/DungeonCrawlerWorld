using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Diagnostics;
using Game.Floors;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.StatusEffects;
using Game.World;

namespace DungeonCrawlerWorld;

/// <summary>
/// Bundles the world/simulation state GameLoop needs across Initialize/Update -- World, the ECS
/// context modules run against, and the composition-root-owned pieces around them (MathUtility,
/// the buffered EntityMovedEvent queue, the Crawler-number allocator, the player activity log).
/// ActionCatalog/ItemCatalog ride along too even though GameLoop itself only ever hands them
/// straight to ShellBootstrapper.Build -- absorbing WorldSessionBootstrapper's own
/// GameBootstrapResult here means GameLoop never needs to keep that intermediate result around
/// itself.
///
/// Mirrors PresentationContext/ShellContext's own shape (an immutable bundle produced by a single
/// Build call) -- the one this one doesn't share with those two is that it's built from a real
/// multi-step sequence with a genuine internal ordering constraint (World must exist before
/// WorldSessionBootstrapper's own GameBootstrapper.Build call, and PlayerActivityLog must
/// subscribe before CreatePlayer publishes the player's spawn EntityMovedEvent), not just several
/// independently constructed services.
/// </summary>
public sealed record WorldSessionContext(
    World World,
    EcsContext EcsContext,
    MathUtility MathUtility,
    FrameEventBuffer<EntityMovedEvent> MovedEntities,
    UniqueNumberAllocator CrawlerNumberAllocator,
    ActionCatalog ActionCatalog,
    ItemCatalog ItemCatalog,
    PlayerActivityLog PlayerActivityLog,
    StatusEffectDisplayRegistry StatusEffectDisplays,
    ReservedEntityIds ReservedEntityIds);
