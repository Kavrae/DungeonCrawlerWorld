using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>Grants loot boxes: the one path every award of a box goes through.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class LootboxActions
{
    /// <summary>Adds quantity of reward's box to entityId's inventory and publishes LootboxGrantedEvent.</summary>
    /// <remarks>A reward with its own Contents is granted as an override of its kind's item, so it keeps the kind's name, sprite and tab but stacks only with boxes carrying the same contents.</remarks>
    public static void Grant(ComponentManager componentManager, LootboxCatalog lootboxCatalog, EventBus eventBus, int entityId, LootboxReward reward, ushort quantity = 1)
    {
        var itemDefinition = lootboxCatalog.GetOrCreateItem(reward);

        if (reward.Contents is null)
        {
            InventoryActions.AddItem(componentManager, entityId, itemDefinition.Id, quantity);
        }
        else
        {
            InventoryActions.AddItemWithOverride(componentManager, entityId, itemDefinition, quantity);
        }

        eventBus.Publish(new LootboxGrantedEvent(entityId, reward, quantity));
    }
}
