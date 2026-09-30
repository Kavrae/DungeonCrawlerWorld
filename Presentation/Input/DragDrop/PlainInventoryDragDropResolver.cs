using Game.Modules.Inventory;
using Game.Modules.Shops;

namespace Presentation.Input.DragDrop;

/// <summary>
/// The input layer's own default drag-drop resolution -- always registered last in
/// UiInputController's resolver list and always claims, so it only ever runs once every
/// feature-specific resolver (Trade, Shop) has already declined the drag. Not itself a "feature"
/// opting in, unlike ShopDragDropResolver/TradeDragDropResolver.
/// </summary>
internal sealed class PlainInventoryDragDropResolver(InventoryCommands inventoryCommands, ShopCommands shopCommands) : IDragDropResolver
{
    public bool TryResolve(in DragDropContext context)
    {
        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            inventoryCommands.TryTransferStack(context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
        }
        else if (context.MergedItemDefinitionId is { } itemDefinitionId)
        {
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
