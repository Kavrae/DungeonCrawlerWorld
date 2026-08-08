using Engine.ECS.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>
/// The "gains an inventory on first item" hook -- called from InventoryActions.AddItem whenever
/// an entity is granted an item for the first time. Mirrors Game.Modules.Mana.ManaGrant.
/// EnsureManaComponentExists: a no-op if the entity already has an InventoryComponent (only the
/// very first item grant actually adds one). Unlike ManaComponent though, this is never removed
/// afterward even once every stack is gone -- see InventoryComponent's own doc comment for why
/// that permanence matters (a future corpse-looting UI needs to distinguish "never had an
/// inventory" from "inventory is currently empty").
/// </summary>
public static class InventoryGrant
{
    public static void EnsureInventoryComponentExists(ComponentManager componentManager, int entityId)
    {
        var inventoryMarkers = componentManager.GetPackedPool<InventoryComponent>();
        if (inventoryMarkers.Has(entityId))
        {
            return;
        }

        componentManager.Merge(entityId, new InventoryComponent());
    }
}
