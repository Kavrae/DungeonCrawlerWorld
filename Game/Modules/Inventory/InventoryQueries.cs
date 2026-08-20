using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Provides static query operations for inventory-related data.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class InventoryQueries
{
    /// <summary>Clears destination, then appends every stack entityId currently owns.</summary>
    public static void CopyStacksForEntity(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, List<InventoryItemStackComponent> destination) =>
        stacks.CopyAll(entityId, destination);

    /// <summary>Determines whether the specified entity's inventory is disabled.</summary>
    /// <remarks>This is different from an entity not containing an inventory.</remarks>
    /// <param name="disabledPool">The pool of disabled inventory components.</param>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <returns><c>true</c> if the inventory is disabled; otherwise, <c>false</c>.</returns>
    public static bool IsInventoryDisabled(DirectComponentPool<InventoryDisabledComponent> disabledPool, int entityId) =>
        disabledPool.TryGetReadonly(entityId, out var component) && component.IsDisabled;

    /// <summary>Tries to get the stack of items from their inventory for a given entity and item definition.</summary>
    /// <param name="stacks">The pool of inventory item stack components.</param>
    /// <param name="entityId">The ID of the entity to check.</param>
    /// <param name="itemDefinitionId">The ID of the item definition to look for.</param>
    /// <param name="stack">When this method returns, contains the stack if found; otherwise, the default value.</param>
    /// <returns><c>true</c> if the stack was found; otherwise, <c>false</c>.</returns>
    public static bool TryGetStack(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, Guid itemDefinitionId, out InventoryItemStackComponent stack) =>
        stacks.TryGetFirst(entityId, itemDefinitionId, static (ref readonly InventoryItemStackComponent candidate, Guid id) => candidate.ItemDefinitionId == id, out stack);

    /// <summary>
    /// Resolves the effective ItemDefinition for a stack -- its own Override if diverged, else the
    /// plain catalog lookup by ItemDefinitionId. The chokepoint any consumer that already has a
    /// specific stack in hand should use instead of a raw itemCatalog.TryGet, so a diverged stack's
    /// own current data (e.g. a wand's remaining charges) is what actually gets read.
    /// </summary>
    public static bool TryResolveEffectiveItem(ItemCatalog itemCatalog, in InventoryItemStackComponent stack, out ItemDefinition definition)
    {
        if (stack.Override is { } overrideDefinition)
        {
            definition = overrideDefinition;
            return true;
        }

        return itemCatalog.TryGet(stack.ItemDefinitionId, out definition!);
    }

    /// <summary>
    /// Finds one specific stack by its stable StackInstanceId -- a manual dense-index walk over
    /// entityId's own chain (MultiComponentPool has no id-indexed direct lookup), the same pattern
    /// AbilityScoreEffects.SetBaseValue already uses to find one matching instance among several.
    /// What a hotkey binding or an in-flight activation resolves through.
    /// </summary>
    public static bool TryFindByStackInstanceId(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, Guid stackInstanceId, out InventoryItemStackComponent stack)
    {
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            ref readonly var candidate = ref stacks.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.StackInstanceId == stackInstanceId)
            {
                stack = candidate;
                return true;
            }
        }

        stack = default;
        return false;
    }
}
