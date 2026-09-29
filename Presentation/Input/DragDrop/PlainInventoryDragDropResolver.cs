using Engine.Events;
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
internal sealed class PlainInventoryDragDropResolver(ItemCatalog itemCatalog, IPlayerQuery playerQuery, EventBus eventBus) : IDragDropResolver
{
    public bool TryResolve(in DragDropContext context)
    {
        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            InventoryActions.TryTransferStack(context.ComponentManager, itemCatalog, context.OriginEntityId, context.DestinationEntityId, stackInstanceId, playerQuery);
        }
        else if (context.MergedItemDefinitionId is { } itemDefinitionId)
        {
            InventoryActions.TryTransferAllStacksOfItem(context.ComponentManager, itemCatalog, context.OriginEntityId, context.DestinationEntityId, itemDefinitionId, playerQuery);
        }
        else if (context.CurrencyType is { } currencyType)
        {
            // Unconditional -- ShopActions.TryGiveCurrencyToShop already checks internally whether
            // the destination is shop-registered before publishing GoldGivenToShopEvent, so this
            // one call degrades to a plain transfer when it isn't. ShopDragDropResolver has already
            // claimed and refused any shop-*origin* currency drag before this resolver ever runs.
            ShopActions.TryGiveCurrencyToShop(context.ComponentManager, eventBus, context.OriginEntityId, context.DestinationEntityId, currencyType);
        }

        return true;
    }
}
