using Engine.ECS.Systems;
using Game.Modules.Auras;
using Game.Modules.ProcessingTier;
using Game.Spawning;
using Game.World;

namespace Game.Bootstrap;

/// <summary>The machinery a game session's bootstrap, streaming, admin tools and inspection work with, kept apart from what ordinary UI reads.</summary>
/// <remarks>A grouping for readability, not an access control.</remarks>
public sealed class GameSessionInternals(
    EntityFactory factory,
    SpawnRecordRebuilder spawnRecordRebuilder,
    EntityTeleporter teleporter,
    ProcessingTierResolver processingTierResolver,
    LocalTierRoster localTierRoster,
    FrameEventBuffer<EntityMovedEvent> movedEntities,
    AuraField auraField)
{
    /// <summary>The session's one spawn path.</summary>
    public EntityFactory Factory { get; } = factory;

    public CreatureSkeletons Skeletons { get; } = factory.Skeletons;

    /// <summary>Rebuilds a creature from its spawn record in a staging world, for inspecting one that is still a skeleton.</summary>
    public SpawnRecordRebuilder SpawnRecordRebuilder { get; } = spawnRecordRebuilder;

    public EntityTeleporter Teleporter { get; } = teleporter;

    public ProcessingTierResolver ProcessingTierResolver { get; } = processingTierResolver;

    public LocalTierRoster LocalTierRoster { get; } = localTierRoster;

    /// <summary>MovementSystem's confirmed moves this frame -- see GameModuleContext.MovedEntities.</summary>
    public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; } = movedEntities;

    /// <summary>Where every aura reaches -- see GameModuleContext.AuraField. Bootstrap builds it once the floor is populated, so its terrain scan is a startup step rather than part of the first frame.</summary>
    public AuraField AuraField { get; } = auraField;
}
