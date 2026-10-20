using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;

namespace Game.Spawning;

/// <summary>Rebuilds what a spawn record produces in a staging world of its own, for reading an entity's defaults without building it in the real one.</summary>
/// <remarks>
/// The staging world registers every module's pools but never runs a system, so nothing built there
/// is simulated, placed, tiered or seen by anything in the real world. Each rebuild creates one
/// staging entity and destroys it before returning. The staging world is built when this is made, or,
/// for a host that may never rebuild anything (the IDE), on the first rebuild.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public sealed class SpawnRecordRebuilder
{
    private readonly Lazy<(EcsContext Staging, EntityBuilder Builder)> _stagingWorld;

    /// <param name="buildStagingWorld">Builds the staging world.</param>
    /// <param name="createBuilder">Builds into the staging world's pools, given its key table, with the session's own blueprint definitions, so a spawn record means the same blueprint in both worlds, including one registered after the staging world was built.</param>
    /// <param name="buildsStagingWorldOnFirstRebuild">False builds the staging world now; true leaves it until something rebuilds a record, and never builds it if nothing does.</param>
    public SpawnRecordRebuilder(Func<EcsContext> buildStagingWorld, Func<EntityKeys, EntityBuilder> createBuilder, bool buildsStagingWorldOnFirstRebuild)
    {
        _stagingWorld = buildsStagingWorldOnFirstRebuild
            ? new Lazy<(EcsContext Staging, EntityBuilder Builder)>(BuildStagingWorld)
            : new Lazy<(EcsContext Staging, EntityBuilder Builder)>(BuildStagingWorld());

        (EcsContext Staging, EntityBuilder Builder) BuildStagingWorld()
        {
            var staging = buildStagingWorld();
            return (staging, createBuilder(staging.EntityManager.Keys));
        }
    }

    /// <summary>Whether the staging world has been built.</summary>
    public bool IsStagingWorldBuilt => _stagingWorld.IsValueCreated;

    /// <summary>Builds record's creature on a staging entity, hands it to read, then destroys it.</summary>
    /// <remarks>The staging entity only lives for the duration of read -- read copies out whatever it needs.</remarks>
    public void Rebuild(SpawnRecordComponent record, Action<ComponentManager, int> read)
    {
        var (staging, builder) = _stagingWorld.Value;
        var entityId = staging.EntityManager.CreateEntity();
        try
        {
            builder.Build(staging.ComponentManager, entityId, record.BlueprintId, record.Seed, now: 0, record.Flags);
            read(staging.ComponentManager, entityId);
        }
        finally
        {
            staging.EntityManager.DestroyEntity(entityId);
        }
    }
}
