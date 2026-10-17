using Engine.ECS.Components;
using Engine.ECS.Context;

namespace Game.Spawning;

/// <summary>Rebuilds what a spawn record produces in a staging world of its own, for reading an entity's defaults without building it in the real one.</summary>
/// <remarks>
/// The staging world registers every module's pools but never runs a system, so nothing built there
/// is simulated, placed, tiered or seen by anything in the real world. Each rebuild creates one
/// staging entity and destroys it before returning.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
/// <param name="builder">Builds into the staging world's pools with the session's own blueprint definitions, so a spawn record means the same blueprint in both worlds, including one registered after the staging world was built.</param>
public sealed class SpawnRecordRebuilder(EcsContext staging, EntityBuilder builder)
{
    /// <summary>Builds record's creature on a staging entity, hands it to read, then destroys it.</summary>
    /// <remarks>The staging entity only lives for the duration of read -- read copies out whatever it needs.</remarks>
    public void Rebuild(SpawnRecordComponent record, Action<ComponentManager, int> read)
    {
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
