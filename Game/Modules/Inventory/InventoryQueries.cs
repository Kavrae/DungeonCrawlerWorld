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
}
