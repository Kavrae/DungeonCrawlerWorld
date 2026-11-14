using Engine.ECS.Components;

namespace Game.Spawning;

/// <summary>Something a module adds to every build, after the blueprint's own parts: what it keeps per entity from what the entity was built from.</summary>
/// <remarks>
/// For state a module derives from a whole composition rather than from one part's Build step -- NpcBehavior's decision
/// component, from the composition's behavior set -- so a build without the module never touches its pool. Registered on
/// the build's EntityFactory (AddBuildStep) in RegisterBehavior, before anything spawns.
/// </remarks>
public interface IEntityBuildStep
{
    /// <summary>Runs once entityId is built (EntityBuilder.BuildComplete).</summary>
    void Built(ComponentManager componentManager, int entityId);

    /// <summary>Runs once a blueprint has been applied to entityId (EntityBuilder.Apply).</summary>
    void Applied(ComponentManager componentManager, int entityId);
}
