using Game.Spawning;

namespace Game.Areas;

/// <summary>Spawns what an area instance planned, a unique placement as the roster allows -- on the main thread, so two plans made at once can never both place one unique.</summary>
public static class AreaSpawner
{
    /// <summary>Spawns planned: a plain spawn as it is; a unique's blueprint when none lives and none was killed this session, recorded in the roster, else its fallback, if any.</summary>
    /// <returns>The entity spawned, or EntityFactory.NoEntity when nothing was.</returns>
    public static int Spawn(EntityFactory factory, UniqueEntityRoster uniqueEntities, in AreaPlannedSpawn planned)
    {
        if (planned.UniqueBlueprint is not { } uniqueBlueprint)
        {
            return factory.Spawn(planned.Request);
        }

        if (uniqueEntities.IsAvailable(uniqueBlueprint))
        {
            var entityId = factory.Spawn(planned.Request);
            if (entityId != EntityFactory.NoEntity)
            {
                uniqueEntities.Placed(uniqueBlueprint, entityId);
            }

            return entityId;
        }

        return planned.Fallback is { } fallback ? factory.Spawn(fallback) : EntityFactory.NoEntity;
    }
}
