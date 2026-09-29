using Engine.ECS.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Activators;

namespace Game.Modules.Inventory;

/// <summary>Grants an item the way its kind is granted: a wand has its charges baked from the recipient's Intelligence, anything else is added as-is.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemGrants
{
    /// <summary>Adds quantity of definition to entityId's inventory through the grant path its kind needs.</summary>
    /// <returns>The StackInstanceId of whichever stack the last granted unit ended up in.</returns>
    public static uint Grant(ComponentManager componentManager, int entityId, ItemDefinition definition, ushort quantity) =>
        definition.Activator is WandActivator
            ? WandGrantEffects.Grant(componentManager, componentManager.GetPackedPool<AbilityScoresComponent>(), entityId, definition, quantity)
            : InventoryActions.AddItem(componentManager, entityId, definition.Id, quantity);
}
