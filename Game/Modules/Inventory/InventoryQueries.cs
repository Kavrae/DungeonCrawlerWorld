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
}
