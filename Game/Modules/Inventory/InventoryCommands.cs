using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.Inventory;

/// <summary>A session's inventory moves, for code outside a system: InventoryActions with the session's pools, catalog and player bound.</summary>
public sealed class InventoryCommands(ComponentManager componentManager, ItemCatalog itemCatalog, IPlayerQuery playerQuery)
{
    /// <inheritdoc cref="InventoryActions.TryTransferStack"/>
    public bool TryTransferStack(int sourceEntityId, int destinationEntityId, uint stackInstanceId) =>
        InventoryActions.TryTransferStack(componentManager, itemCatalog, sourceEntityId, destinationEntityId, stackInstanceId, playerQuery);

    /// <inheritdoc cref="InventoryActions.TryTransferAllStacksOfItem"/>
    public bool TryTransferAllStacksOfItem(int sourceEntityId, int destinationEntityId, Guid itemDefinitionId) =>
        InventoryActions.TryTransferAllStacksOfItem(componentManager, itemCatalog, sourceEntityId, destinationEntityId, itemDefinitionId, playerQuery);

    /// <inheritdoc cref="InventoryActions.MergeIntoEquivalentStack"/>
    public uint MergeIntoEquivalentStack(int entityId, uint stackInstanceId) =>
        InventoryActions.MergeIntoEquivalentStack(componentManager, entityId, stackInstanceId);
}
