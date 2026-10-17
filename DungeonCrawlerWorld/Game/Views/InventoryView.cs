using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Views;

/// <summary>What an entity carries, read for display and for checking a gesture before it becomes a command.</summary>
public sealed class InventoryView(ComponentManager componentManager, ItemCatalog itemCatalog)
{
    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<InventoryDisabledComponent> _disabled = componentManager.GetPackedPool<InventoryDisabledComponent>();

    /// <summary>entityId's stack stackInstanceId, if it holds one.</summary>
    public bool TryGetStack(int entityId, uint stackInstanceId, out InventoryItemStackComponent stack) =>
        InventoryQueries.TryFindByStackInstanceId(_stacks, entityId, stackInstanceId, out stack);

    /// <summary>Replaces destination's contents with every stack entityId holds.</summary>
    public void CopyStacks(int entityId, List<InventoryItemStackComponent> destination) =>
        InventoryQueries.CopyStacksForEntity(_stacks, entityId, destination);

    /// <summary>How many stacks entityId holds.</summary>
    public int CountStacks(int entityId) => _stacks.CountForEntity(entityId);

    /// <summary>Changes whenever any of entityId's stacks is added, updated or removed.</summary>
    public uint GetVersion(int entityId) => _stacks.GetEntityVersion(entityId);

    /// <inheritdoc cref="InventoryQueries.IsInventoryDisabled"/>
    public bool IsInventoryDisabled(int entityId) => InventoryQueries.IsInventoryDisabled(_disabled, entityId);

    /// <inheritdoc cref="InventoryTagQueries.GetItemCategoryCounts"/>
    public List<(GameplayTag Tag, int Count)> GetItemCategoryCounts(int entityId, GameplayTagRegistry gameplayTags) => InventoryTagQueries.GetItemCategoryCounts(componentManager, itemCatalog, gameplayTags, entityId);
}
