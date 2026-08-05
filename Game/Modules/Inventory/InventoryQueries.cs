using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Read-side counterpart to InventoryActions -- same shape as NonBlockingQueries, walking a MultiComponentPool's dense per-entity chain.</summary>
public static class InventoryQueries
{
    /// <summary>Clears destination, then appends every stack entityId currently owns.</summary>
    public static void CopyStacksForEntity(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, List<InventoryItemStackComponent> destination)
    {
        destination.Clear();

        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            destination.Add(stacks.GetReadonlyByDenseIndex(denseIndex));
        }
    }

    public static bool IsInventoryDisabled(DirectComponentPool<InventoryDisabledComponent> disabledPool, int entityId) =>
        disabledPool.TryGetReadonly(entityId, out var component) && component.IsDisabled;

    /// <summary>Single-stack lookup by exact ItemDefinitionId match -- unlike CopyStacksForEntity, doesn't walk/copy the entity's whole chain. ConsumableActivationSystem's main use: "does this entity actually still have the item it's trying to activate."</summary>
    public static bool TryGetStack(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, Guid itemDefinitionId, out InventoryItemStackComponent stack)
    {
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            var candidate = stacks.GetReadonlyByDenseIndex(denseIndex);
            if (candidate.ItemDefinitionId == itemDefinitionId)
            {
                stack = candidate;
                return true;
            }
        }

        stack = default;
        return false;
    }
}
