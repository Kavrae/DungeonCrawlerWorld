using Engine.ECS.Components;
using Game.Modules.Actions.Components;
using Game.Modules.Mana;

namespace Game.Modules.Actions;

/// <summary>
/// Write surface for granting an action instance to an entity -- wraps the plain
/// ActionInstanceComponent merge every blueprint used to do directly with the "gains mana on
/// first mana-draining action" hook (ManaGrant.EnsureManaComponentExists), so any call site granting an action
/// automatically also grants mana the first time that action drains its user's (ManaUse.DrainsUsersMana, read
/// from the definition the entity will use), rather than every blueprint having to remember to call ManaGrant
/// itself. Callers must grant the entity's ability scores before granting such an action --
/// ManaGrant.EnsureManaComponentExists sizes MaximumMana off Intelligence's Total, which has to already exist to read.
/// </summary>
public static class ActionGrantEffects
{
    /// <remarks>A new grant is always ready to use -- its cooldown deadline starts at 0 (see ActionInstanceComponent.CooldownReadyAtFrame).</remarks>
    /// <param name="actions">Where an action with no override is read from.</param>
    public static void Grant(ComponentManager componentManager, ActionCatalog actions, int entityId, Guid actionId, ActionDefinition? overrideDefinition)
    {
        componentManager.Merge(entityId, new ActionInstanceComponent(actionId, overrideDefinition));

        if ((overrideDefinition ?? (actions.TryGet(actionId, out var definition) ? definition : null)) is { } action && ManaUse.DrainsUsersMana(action))
        {
            ManaGrant.EnsureManaComponentExists(componentManager, entityId);
        }
    }
}
