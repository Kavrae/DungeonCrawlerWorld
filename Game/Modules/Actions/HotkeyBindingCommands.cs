using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>Binds and unbinds an entity's hotkey slots, publishing the bound events a player action raises.</summary>
/// <remarks>
/// A slot binds to at most one of {action, item} at a time (see IHotkeySlotBinding), so binding either kind clears
/// both first. Binding an item is a reference, not a transfer. Whether a slot is unlocked is the caller's rule, not
/// this one's: the hotbar refuses a locked slot as a drop target before it gets here. Spawn-time setup (PlayerKit's
/// starting binds) writes the pools directly and publishes nothing, since it isn't a player action.
/// </remarks>
public sealed class HotkeyBindingCommands(ComponentManager componentManager, ItemCatalog itemCatalog, EventBus eventBus)
{
    private readonly MultiComponentPool<ActionHotkeyBindingComponent> _actionHotkeyBindings = componentManager.GetMultiPool<ActionHotkeyBindingComponent>();
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    /// <summary>Binds slot to entityId's stack stackInstanceId and publishes ItemHotkeyBoundEvent; false, changing nothing, when the item can never be bound (ItemHotkeyBindingQueries.CanBind).</summary>
    public bool TryBindItem(int entityId, HotkeySlot slot, uint stackInstanceId)
    {
        var stackFound = InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, entityId, stackInstanceId, out var stack);
        if (stackFound &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) &&
            !ItemHotkeyBindingQueries.CanBind(item))
        {
            return false;
        }

        ClearSlot(entityId, slot);
        _itemHotkeyBindings.Add(entityId, new ItemHotkeyBindingComponent(slot, stackInstanceId));

        if (stackFound)
        {
            eventBus.Publish(new ItemHotkeyBoundEvent(entityId, slot, stack.ItemDefinitionId));
        }

        return true;
    }

    /// <summary>Binds slot to actionId and publishes ActionHotkeyBoundEvent.</summary>
    public void BindAction(int entityId, HotkeySlot slot, Guid actionId)
    {
        ClearSlot(entityId, slot);
        _actionHotkeyBindings.Add(entityId, new ActionHotkeyBindingComponent(slot, actionId));
        eventBus.Publish(new ActionHotkeyBoundEvent(entityId, slot, actionId));
    }

    /// <summary>Removes slot's item binding, if any.</summary>
    public void UnbindItem(int entityId, HotkeySlot slot) => ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, entityId, slot);

    /// <summary>Removes slot's action binding, if any.</summary>
    public void UnbindAction(int entityId, HotkeySlot slot) => ActionHotkeyBindingQueries.Unbind(_actionHotkeyBindings, entityId, slot);

    /// <summary>Removes slot's binding of either kind.</summary>
    public void ClearSlot(int entityId, HotkeySlot slot)
    {
        UnbindAction(entityId, slot);
        UnbindItem(entityId, slot);
    }
}
