using Engine.ECS.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Write-side counterpart to InventoryQueries -- mutates an entity's inventory storage.</summary>
public static class InventoryActions
{
    /// <summary>
    /// Grants quantity of itemDefinitionId, stacking onto an existing matching stack if one
    /// exists rather than always creating a new one -- this is the "identical items grouped with
    /// a count" behavior. The single chokepoint every item grant goes through (starting kits,
    /// future loot drops), so it's also where InventoryGrant.EnsureInventoryComponentExists runs
    /// -- every caller gets the "gains an inventory on first item" behavior for free, the player
    /// included, with no per-call-site handling needed.
    /// </summary>
    public static void AddItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId, ushort quantity)
    {
        InventoryGrant.EnsureInventoryComponentExists(componentManager, entityId);

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

    /// <summary>
    /// Ticks the matching stack's Quantity down by 1, removing the stack entirely once it hits
    /// 0 (same "no instance for this item" empty-state convention InventoryItemStackComponent's
    /// own doc comment describes) -- called by ConsumableActivationSystem after every successful
    /// activation. A no-op if the entity doesn't actually have the item (defense-in-depth; the
    /// caller is expected to have already checked).
    /// </summary>
    public static void ConsumeItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        if (!InventoryQueries.TryGetStack(stacks, entityId, itemDefinitionId, out var stack))
        {
            return;
        }

        if (stack.Quantity <= 1)
        {
            stacks.RemoveFirst(entityId, itemDefinitionId, static (ref readonly s, id) => s.ItemDefinitionId == id);
            return;
        }

        stacks.TryUpdateFirst(
            entityId,
            itemDefinitionId,
            static (ref readonly s, id) => s.ItemDefinitionId == id,
            static (ref s, id) => s.Quantity--);
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
