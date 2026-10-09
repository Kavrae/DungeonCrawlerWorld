using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.World;

namespace Presentation.Input.DragDrop;

/// <summary>
/// The input layer's own default drag-drop resolution -- always registered last in
/// UiInputController's resolver list and always claims, so it only ever runs once every
/// feature-specific resolver (Trade, Shop) has already declined the drag. Not itself a "feature"
/// opting in, unlike ShopDragDropResolver/TradeDragDropResolver.
/// </summary>
/// <remarks>
/// With trades and shops already claimed, an item dragged from another entity onto the player is
/// taken from a corpse or a container -- looting, which merges into the player's interchangeable
/// stacks. Every other item drag (a Give) moves the stack whole.
/// </remarks>
internal sealed class PlainInventoryDragDropResolver(InventoryCommands inventoryCommands, ShopCommands shopCommands, IPlayerQuery playerQuery) : IDragDropResolver
{
    public bool TryResolve(in DragDropContext context)
    {
        var isLooting = context.DestinationEntityId == playerQuery.PlayerEntityId && context.OriginEntityId != playerQuery.PlayerEntityId;

        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            if (isLooting)
            {
                inventoryCommands.TryLootStack(context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
                return true;
            }

            inventoryCommands.TryTransferStack(context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
        }
        else if (context.MergedItemDefinitionId is { } itemDefinitionId)
        {
            if (isLooting)
            {
                inventoryCommands.LootAllStacksOfItem(context.OriginEntityId, context.DestinationEntityId, itemDefinitionId);
                return true;
            }

            inventoryCommands.TryTransferAllStacksOfItem(context.OriginEntityId, context.DestinationEntityId, itemDefinitionId);
        }
        else if (context.CurrencyType is { } currencyType)
        {
            // Unconditional -- TryGiveCurrencyToShop already checks internally whether
            // the destination is shop-registered before publishing GoldGivenToShopEvent, so this
            // one call degrades to a plain transfer when it isn't. ShopDragDropResolver has already
            // claimed and refused any shop-*origin* currency drag before this resolver ever runs.
            shopCommands.TryGiveCurrencyToShop(context.OriginEntityId, context.DestinationEntityId, currencyType);
        }

        return true;
    }
}
