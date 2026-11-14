using Engine.ECS.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Spawning;

namespace Game.Modules.NpcBehavior;

/// <summary>Gives an entity that has something to decide its NpcDecisionComponent, and keeps the set it decides with current as parts are applied to it.</summary>
/// <remarks>
/// The set is its spawn blueprint's, or the composition of it and its applied parts (NpcBehaviorSets.SetIdFor). An entity
/// whose set is empty -- a chest, a shop -- holds no component, so the decision system never visits it. Runs after the
/// blueprint's own parts, and writes into whichever world it is building in -- the session's, or the staging world a
/// spawn record is rebuilt in.
/// </remarks>
internal sealed class NpcDecisionBuildStep(NpcBehaviorSets sets) : IEntityBuildStep
{
    public void Built(ComponentManager componentManager, int entityId) => Assign(componentManager, entityId);

    public void Applied(ComponentManager componentManager, int entityId) => Assign(componentManager, entityId);

    private void Assign(ComponentManager componentManager, int entityId)
    {
        var decisions = componentManager.GetPackedPool<NpcDecisionComponent>();
        var setId = sets.SetIdFor(entityId, componentManager.GetDirectPool<SpawnRecordComponent>(), componentManager.GetMultiPool<AppliedBlueprintComponent>());
        if (sets.Get(setId).IsEmpty)
        {
            decisions.Remove(entityId);
            return;
        }

        using (var decision = decisions.TryWrite(entityId))
        {
            if (decision.Found)
            {
                decision.Value.BehaviorSetId = setId;
                return;
            }
        }

        decisions.Add(entityId, new NpcDecisionComponent(setId));
    }
}
