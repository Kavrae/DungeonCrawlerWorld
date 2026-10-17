using System.Diagnostics.CodeAnalysis;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Views;

/// <summary>Turns an ActivatableReference into what the entity actually uses: its effective action, or the effective item in the stack it holds.</summary>
/// <remarks>
/// The one place a view resolves a windup or a toggle into its definition, so two views can't disagree about
/// it -- an action is the entity's own (its override, if it has one, through EntityActions), never the bare
/// catalog entry. Lives in Views because it reads items, which Actions can't.
/// </remarks>
/// <param name="itemCatalog">Resolves item stacks; null (a test without items) resolves none.</param>
public sealed class ActivatableLookup(EntityActions entityActions, ItemCatalog? itemCatalog, ComponentManager componentManager)
{
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    /// <summary>The definition reference names for entityId; false when it is gone (an action it no longer has, a stack it no longer holds).</summary>
    public bool TryGetDefinition(int entityId, in ActivatableReference reference, out ActivatableDefinition definition)
    {
        if (reference.Kind == ActivatableKind.Action)
        {
            var hasAction = entityActions.TryGetEffectiveAction(entityId, reference.ActionId, out var action);
            definition = action!;
            return hasAction;
        }

        var hasItem = TryGetItem(entityId, reference.StackInstanceId, out var item);
        definition = item!;
        return hasItem;
    }

    /// <summary>How the definition reference names is activated for entityId; false when it is gone or, for an item, has no activator.</summary>
    public bool TryGetActivator(int entityId, in ActivatableReference reference, out IActionActivator activator)
    {
        if (reference.Kind == ActivatableKind.Action)
        {
            var hasAction = entityActions.TryGetEffectiveAction(entityId, reference.ActionId, out var action);
            activator = action?.Activator!;
            return hasAction;
        }

        if (TryGetItem(entityId, reference.StackInstanceId, out var item) && item.Activator is { } itemActivator)
        {
            activator = itemActivator;
            return true;
        }

        activator = null!;
        return false;
    }

    private bool TryGetItem(int entityId, uint stackInstanceId, [NotNullWhen(true)] out ItemDefinition? item)
    {
        if (itemCatalog is not null &&
            InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, entityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var resolved))
        {
            item = resolved;
            return true;
        }

        item = null;
        return false;
    }
}
