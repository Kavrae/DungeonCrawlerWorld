using Engine.ECS.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Write-side counterpart to InventoryQueries -- mutates an entity's inventory storage.</summary>
public static class InventoryActions
{
    /// <summary>Grants quantity of itemDefinitionId, stacking onto an existing matching stack if one exists rather than always creating a new one -- this is the "identical items grouped with a count" behavior.</summary>
    public static void AddItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId, int quantity)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var stacked = stacks.TryUpdateFirst(
            entityId,
            (itemDefinitionId, quantity),
            static (ref readonly stack, state) => stack.ItemDefinitionId == state.itemDefinitionId,
            static (ref stack, state) => stack.Quantity += state.quantity);

        if (!stacked)
        {
            stacks.Add(entityId, new InventoryItemStackComponent(itemDefinitionId, quantity));
        }
    }

    /// <summary>Disables/enables one specific stack (e.g. an item withheld until some later trigger) -- distinct from SetInventoryDisabled below, which disables the whole inventory.</summary>
    public static void SetStackDisabled(ComponentManager componentManager, int entityId, Guid itemDefinitionId, bool disabled)
    {
        componentManager.GetMultiPool<InventoryItemStackComponent>().TryUpdateFirst(
            entityId,
            (itemDefinitionId, disabled),
            static (ref readonly stack, state) => stack.ItemDefinitionId == state.itemDefinitionId,
            static (ref stack, state) => stack.IsDisabled = state.disabled);
    }

    /// <summary>Disables/enables an entity's whole inventory -- items still exist and can still be granted while disabled, but the management window can't be opened (see InventoryFolderController).</summary>
    public static void SetInventoryDisabled(ComponentManager componentManager, int entityId, bool disabled) =>
        componentManager.Merge(entityId, new InventoryDisabledComponent(disabled));
}
