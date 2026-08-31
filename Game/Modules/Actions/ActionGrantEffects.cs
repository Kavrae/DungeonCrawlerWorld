using Engine.ECS.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Mana;

namespace Game.Modules.Actions;

/// <summary>
/// Write surface for granting an action instance to an entity -- wraps the plain
/// ActionInstanceComponent merge every blueprint used to do directly with the "gains mana on
/// first mana-costing action" hook (ManaGrant.EnsureManaComponentExists), so any call site granting an action
/// automatically also grants mana the first time that action actually costs some, rather than
/// every blueprint having to remember to call ManaGrant itself. Callers must grant the entity's
/// ability scores before calling this for a manaCost > 0 action -- ManaGrant.EnsureManaComponentExists sizes
/// MaximumMana off Intelligence's Total, which has to already exist to read.
/// </summary>
public static class ActionGrantEffects
{
    public static void Grant(ComponentManager componentManager, int entityId, Guid actionId, ushort manaCost, ActionDefinition? overrideDefinition, ushort cooldownFramesRemaining)
    {
        componentManager.Merge(entityId, new ActionInstanceComponent(actionId, overrideDefinition, cooldownFramesRemaining));

        if (manaCost > 0)
        {
            ManaGrant.EnsureManaComponentExists(componentManager, entityId);
        }
    }
}
