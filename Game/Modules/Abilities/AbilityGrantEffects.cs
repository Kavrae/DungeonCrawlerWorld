using Engine.ECS.Components;
using Game.Modules.Abilities.Components;
using Game.Modules.Mana;

namespace Game.Modules.Abilities;

/// <summary>
/// Write surface for granting an ability instance to an entity -- wraps the plain
/// AbilityInstanceComponent merge every blueprint used to do directly with the "gains mana on
/// first mana-costing ability" hook (ManaGrant.EnsureManaComponentExists), so any call site granting an ability
/// automatically also grants mana the first time that ability actually costs some, rather than
/// every blueprint having to remember to call ManaGrant itself. Callers must grant the entity's
/// ability scores before calling this for a manaCost > 0 ability -- ManaGrant.EnsureManaComponentExists sizes
/// MaximumMana off Intelligence's Total, which has to already exist to read.
/// </summary>
public static class AbilityGrantEffects
{
    public static void Grant(ComponentManager componentManager, int entityId, Guid abilityId, short manaCost, short damageAmount, short cooldownFramesRemaining)
    {
        componentManager.Merge(entityId, new AbilityInstanceComponent(abilityId, damageAmount, cooldownFramesRemaining));

        if (manaCost > 0)
        {
            ManaGrant.EnsureManaComponentExists(componentManager, entityId);
        }
    }
}
