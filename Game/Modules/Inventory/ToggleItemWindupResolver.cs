using Engine.ECS.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Resolves an item's windup: lights the toggle item the windup was started for.</summary>
/// <remarks>
/// A Delayed toggle item winds up only to turn on, so resolving means lighting one unit of the stack
/// -- if the holder still has it and it is still unlit. A stack that left the holder during the
/// windup (traded, given away) lights nothing, and neither does one already lit some other way.
/// What turning on takes was applied when the windup began.
/// </remarks>
internal sealed class ToggleItemWindupResolver(ComponentManager componentManager, ItemCatalog itemCatalog) : IItemWindupResolver
{
    public void Resolve(int entityId, in PendingWindupComponent windup, long now)
    {
        if (!InventoryQueries.TryFindByStackInstanceId(componentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, windup.Activatable.StackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            item.Activator is not ToggleItemActivator { IsToggledOn: false })
        {
            return;
        }

        ToggleItemActions.TryToggle(componentManager, itemCatalog, entityId, windup.Activatable.StackInstanceId, out _, windup.ActivatedFromSlot);
    }
}
